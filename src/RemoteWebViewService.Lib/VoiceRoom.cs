using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView;

internal sealed class VoiceRoom(string hostKey, CancellationToken hostLifetime)
{
    private CancellationToken HostLifetime { get; } = hostLifetime;
    private const int MaxMessageSize = 16 * 1024;
    private readonly string _controllerKey = NewKey();
    private readonly string _pageKey = NewKey();
    private readonly object _gate = new();
    private Peer? _host;
    private Peer? _controller;
    private bool _pageGranted;
    private bool _pageClaimed;
    private bool _mirrorGranted;
    private Guid _callId;
    private bool _controllerConnected;
    private CallState _callState;

    private enum CallState { Idle, Ringing, Accepted }

    internal sealed class Peer(bool isHost)
    {
        public bool IsHost { get; } = isHost;
        public Channel<byte[]> Outbox { get; } = Channel.CreateBounded<byte[]>(64);
    }

    private static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static bool Matches(string expected, string actual) =>
        actual.Length == expected.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    internal static VoiceRoom? Create(string key, CancellationToken hostLifetime) =>
        key.Length == 64 && Array.TrueForAll(key.ToCharArray(), Uri.IsHexDigit) ? new(key, hostLifetime) : null;

    private static string CookieName(string id) => $"rwv-voice-{id}";

    internal void GrantController(HttpContext context, string id)
    {
        lock (_gate)
        {
            if (_pageGranted)
                return;
            _pageGranted = true;
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Cookies.Append(CookieName(id), _pageKey, new CookieOptions
        {
            Path = $"/{id}",
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(1)
        });
    }

    internal string? TakeControllerPage(HttpContext context, string id)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName(id), out var key) || !Matches(_pageKey, key))
            return null;

        lock (_gate)
        {
            if (_pageClaimed)
                return null;
            _pageClaimed = true;
        }

        context.Response.Headers.CacheControl = "no-store";
        context.Response.Cookies.Delete(CookieName(id), new CookieOptions { Path = $"/{id}", Secure = true });
        return _controllerKey;
    }

    /// <summary>
    /// The remote user's key for a /mirror page, when the owner's surface holds /app. Given to any mirror page that
    /// loads while nobody holds the remote end of the call: the remote user's page gets it again after a refresh, and a
    /// page that loaded but never connected (a chat app fetching the link for a preview) does not use it up. While a
    /// remote end is connected nobody else gets it, so a second person opening the link cannot take over the call.
    /// </summary>
    internal string? TakeControllerForMirror()
    {
        lock (_gate)
        {
            // The /app page route was used for this session (a session without an owner's surface): that grant is given once.
            if ((_pageGranted || _pageClaimed) && !_mirrorGranted)
                return null;
            if (_controller is not null)
                return null;
            _pageGranted = _pageClaimed = _mirrorGranted = true;
        }
        return _controllerKey;
    }

    internal static string ControllerScript(Guid id, string key) =>
        $"""<script defer src="/voice/client.js" data-session="{id}" data-key="{key}"></script>""";

    internal static RequestDelegate Script() => async context =>
    {
        context.Response.ContentType = "text/javascript; charset=utf-8";
        using var script = typeof(VoiceRoom).Assembly.GetManifestResourceStream("VoiceClient.js")!;
        await script.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
    };

    internal static RequestDelegate HostPage() => async context =>
    {
        if (FindSession(context)?.Voice is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.ContentSecurityPolicy =
            "default-src 'none'; script-src 'self'; connect-src 'self'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'";
        context.Response.Headers["Permissions-Policy"] = "microphone=(self), camera=()";
        await context.Response.WriteAsync($"""
            <!doctype html><html lang="en"><meta charset="utf-8"><title>Remote voice call</title>
            <script defer src="/voice/client.js" data-session="{context.Request.RouteValues["id"]}" data-host="true"></script>
            <body></body></html>
            """).ConfigureAwait(false);
    };

    internal static RequestDelegate Signal() => async context =>
    {
        var session = FindSession(context);
        if (session?.Voice is not VoiceRoom room || !context.WebSockets.IsWebSocketRequest ||
            !string.Equals(context.Request.Headers.Origin, $"https://{context.Request.Host}", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, session.Token, room.HostLifetime);
        try
        {
            using var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[MaxMessageSize];
            var first = await Receive(socket, buffer, handshake.Token).ConfigureAwait(false);
            if (first is null)
                return;

            var key = Encoding.UTF8.GetString(first);
            var peer = room.Join(key);
            if (peer is null)
                return;

            try
            {
                var config = context.RequestServices.GetRequiredService<IConfiguration>();
                var ready = ReadyMessage(config);
                await socket.SendAsync(ready.AsMemory(), WebSocketMessageType.Text, true, lifetime.Token).ConfigureAwait(false);
                var sending = Send(socket, peer, lifetime.Token);
                var receiving = room.ReceiveSignals(socket, peer, buffer, lifetime.Token);
                await Task.WhenAny(sending, receiving).ConfigureAwait(false);
                await lifetime.CancelAsync().ConfigureAwait(false);
                await Task.WhenAll(sending, receiving).ConfigureAwait(false);
            }
            finally
            {
                room.Leave(peer);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or JsonException)
        {
            // A disconnected or malformed client ends its call.
        }
    };

    private static ServiceState? FindSession(HttpContext context)
    {
        if (!Guid.TryParse(context.Request.RouteValues["id"]?.ToString(), out var id))
            return null;
        var sessions = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();
        return sessions.TryGetValue(id.ToString(), out var registration) && registration.Task.IsCompletedSuccessfully
            && !registration.Task.Result.Token.IsCancellationRequested
            && registration.Task.Result.Voice?.HostLifetime.IsCancellationRequested != true ? registration.Task.Result : null;
    }

    // Messages are written with Utf8JsonWriter, not JsonSerializer over anonymous types: the relay is also published trimmed
    // and native (AOT), where reflection-based serialization is unavailable.
    private static byte[] ReadyMessage(IConfiguration config)
    {
        var stun = config["Voice:StunUrl"];
        var turn = config["Voice:TurnUrl"];
        var secret = config["Voice:TurnSecret"];
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("type", "ready");
            w.WriteStartArray("iceServers");
            if (!string.IsNullOrEmpty(stun))
            {
                w.WriteStartObject();
                w.WriteString("urls", stun);
                w.WriteEndObject();
            }
            if (!string.IsNullOrEmpty(turn) && !string.IsNullOrEmpty(secret))
            {
                var username = $"{DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds()}:{NewKey()}";
                var credential = Convert.ToBase64String(HMACSHA1.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(username)));
                w.WriteStartObject();
                w.WriteString("urls", turn);
                w.WriteString("username", username);
                w.WriteString("credential", credential);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] CallMessage(string type, Guid callId)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteString("callId", callId);
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }

    internal Peer? Join(string key)
    {
        lock (_gate)
        {
            if (_host is null && Matches(hostKey, key))
                return _host = new Peer(isHost: true);
            // The /app page's key connects once. A mirror page's key connects again after the previous page has gone
            // (the remote user refreshed), but never while a remote end is connected.
            if ((_mirrorGranted ? _controller is null : !_controllerConnected) && Matches(_controllerKey, key))
            {
                _controllerConnected = true;
                return _controller = new Peer(isHost: false);
            }
            return null;
        }
    }

    internal void Leave(Peer peer)
    {
        lock (_gate)
        {
            var other = peer.IsHost ? _controller : _host;
            if (peer.IsHost)
                _host = null;
            else
                _controller = null;

            if (_callState != CallState.Idle && other is not null)
                Post(other, CallMessage("end", _callId));
            _callState = CallState.Idle;
        }
    }

    private static void Post(Peer peer, byte[] message)
    {
        if (!peer.Outbox.Writer.TryWrite(message))
            peer.Outbox.Writer.TryComplete();
    }

    private void Forward(Peer peer, string? type, Guid callId, byte[] message)
    {
        lock (_gate)
        {
            var other = peer.IsHost ? _controller : _host;
            if (other is null)
            {
                Post(peer, CallMessage("unavailable", callId));
                return;
            }

            if (type != "invite" && callId != _callId)
                return;

            switch (type)
            {
                case "invite" when !peer.IsHost && _callState == CallState.Idle:
                    _callId = callId;
                    _callState = CallState.Ringing;
                    break;
                case "accept" when peer.IsHost && _callState == CallState.Ringing:
                    _callState = CallState.Accepted;
                    break;
                case "offer" when !peer.IsHost && _callState == CallState.Accepted:
                case "answer" when peer.IsHost && _callState == CallState.Accepted:
                case "ice" when _callState == CallState.Accepted:
                    break;
                case "end" when _callState != CallState.Idle:
                    _callState = CallState.Idle;
                    break;
                default:
                    return;
            }

            Post(other, message);
        }
    }

    private async Task ReceiveSignals(WebSocket socket, Peer peer, byte[] buffer, CancellationToken token)
    {
        while (await Receive(socket, buffer, token).ConfigureAwait(false) is byte[] message)
        {
            using var json = JsonDocument.Parse(message);
            if (json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !json.RootElement.TryGetProperty("callId", out var callId) || callId.ValueKind != JsonValueKind.String ||
                !callId.TryGetGuid(out var id) || id == Guid.Empty)
                return;
            Forward(peer, type.GetString(), id, message);
        }
    }

    private static async Task<byte[]?> Receive(WebSocket socket, byte[] buffer, CancellationToken token)
    {
        var count = 0;
        while (count < buffer.Length)
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
            if (result.MessageType != WebSocketMessageType.Text)
                return null;
            count += result.Count;
            if (result.EndOfMessage)
                return buffer.AsSpan(0, count).ToArray();
        }
        return null;
    }

    private static async Task Send(WebSocket socket, Peer peer, CancellationToken token)
    {
        await foreach (var message in peer.Outbox.Reader.ReadAllAsync(token).ConfigureAwait(false))
            await socket.SendAsync(message.AsMemory(), WebSocketMessageType.Text, true, token).ConfigureAwait(false);
    }
}
