using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StaticContentCache.Tests;

public class VoiceRoomTests
{
    [Fact]
    public async Task Only_granted_peers_can_signal_and_host_must_accept_before_offer()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        var hostKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var hostLifetime = new CancellationTokenSource();
        var id = Guid.NewGuid().ToString();
        var room = VoiceRoom.Create(hostKey, hostLifetime.Token)!;
        var grant = new DefaultHttpContext();
        room.GrantController(grant, id);
        var cookie = grant.Response.Headers.SetCookie.ToString().Split(';')[0];

        Assert.Null(room.TakeControllerPage(new DefaultHttpContext(), id));
        var controllerPage = new DefaultHttpContext();
        controllerPage.Request.Headers.Cookie = cookie;
        var controllerKey = Assert.IsType<string>(room.TakeControllerPage(controllerPage, id));
        Assert.Null(room.TakeControllerPage(controllerPage, id));

        var sessions = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        await using var session = new ServiceState(NullLogger<RemoteWebViewService>.Instance, true) { Voice = room };
        sessions[id] = new TaskCompletionSource<ServiceState>();
        sessions[id].SetResult(session);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(sessions);
        await using var app = builder.Build();
        app.UseWebSockets();
        app.MapGet("/voice/{id:guid}/signal", VoiceRoom.Signal());
        await app.StartAsync(token);

        var endpoint = new Uri($"wss://localhost/voice/{id}/signal");
        var client = app.GetTestServer().CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers.Origin = "https://localhost";
        using (var observer = await client.ConnectAsync(endpoint, token))
        {
            await Send(observer, id, token);
            Assert.Null(await Read(observer, token));
        }

        using var host = await client.ConnectAsync(endpoint, token);
        using var controller = await client.ConnectAsync(endpoint, token);
        await Send(host, hostKey, token);
        await Send(controller, controllerKey, token);
        Assert.Equal("ready", await Read(host, token));
        Assert.Equal("ready", await Read(controller, token));

        var call = Guid.NewGuid();
        await SendSignal(controller, "offer", call, token);
        await SendSignal(controller, "invite", call, token);
        Assert.Equal("invite", await Read(host, token));
        await SendSignal(host, "accept", call, token);
        Assert.Equal("accept", await Read(controller, token));
        await SendSignal(controller, "offer", Guid.NewGuid(), token);
        await SendSignal(controller, "offer", call, token);
        Assert.Equal("offer", await Read(host, token));

        await controller.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, token);
        Assert.Equal("end", await Read(host, token));
        await hostLifetime.CancelAsync();
        Assert.Null(await Read(host, token));
        await session.DisposeAsync();
    }

    private static Task SendSignal(WebSocket socket, string type, Guid callId, CancellationToken token) =>
        Send(socket, JsonSerializer.Serialize(new { type, callId }), token);

    private static Task Send(WebSocket socket, string message, CancellationToken token) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message)), WebSocketMessageType.Text, true, token);

    private static async Task<string?> Read(WebSocket socket, CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            var result = await socket.ReceiveAsync(buffer.AsMemory(), token);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            Assert.True(result.EndOfMessage);
            using var json = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
            return json.RootElement.GetProperty("type").GetString();
        }
        catch (Exception ex) when (ex is WebSocketException or IOException && !token.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return null;
        }
    }
}
