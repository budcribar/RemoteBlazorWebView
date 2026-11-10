using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

/// <summary>
/// Sharing control: who controls a session, who may hand it over or take it, what happens when the controller leaves,
/// and the roster every browser is sent. Drives BrowserIPCService directly, with one fake browser stream per connection.
/// </summary>
public class ControlTransferTests
{
    [Fact]
    public async Task The_first_connection_to_send_claims_control()
    {
        await using var s = new Session();
        s.Join("a");

        Assert.Equal("", s.State.PrimaryClientId);
        Assert.True((await s.Send("a", 1)).Success);

        Assert.Equal("a", s.State.PrimaryClientId);
        Assert.Equal(2u, s.State.BrowserIPC.SequenceNum);
    }

    [Fact]
    public async Task Input_from_a_connection_without_control_is_dropped()
    {
        await using var s = new Session();
        s.Join("a");
        s.Join("b");
        await s.Send("a", 1);

        var response = await s.Send("b", 2); // the number the relay expects next, but b does not have control

        Assert.True(response.Success);
        Assert.Equal("a", s.State.PrimaryClientId);
        Assert.Equal(2u, s.State.BrowserIPC.SequenceNum); // not forwarded
        Assert.Empty(s.State.BrowserIPC.MessageDictionary); // and not parked either
    }

    [Fact]
    public async Task Input_that_was_waiting_its_turn_when_control_moved_is_dropped()
    {
        await using var s = new Session();
        var host = s.CaptureHost();
        s.Join("a");
        s.Join("b");
        await s.Send("a", 1);

        // Hold the relay's ordering lock so a's next message passes the control check and then waits.
        var turn = s.State.BrowserIPC.Semaphore;
        await turn.WaitAsync();
        var late = s.Send("a", 2);
        await Task.Delay(100);
        var handOff = s.SetPrimary("a", "b"); // control moves while that message is still waiting
        await WaitFor(() => s.State.PrimaryClientId == "b");
        turn.Release();
        Assert.True((await late).Success);
        await handOff;

        await s.Send("b", 1);
        await WaitFor(() => host.Messages.Contains("m1-b"));
        Assert.DoesNotContain("m2-a", host.Messages);          // not forwarded to the app
        Assert.Empty(s.State.BrowserIPC.MessageDictionary);    // and not parked to take b's number 2 later
        Assert.Equal(2u, s.State.BrowserIPC.SequenceNum);
    }

    [Fact]
    public async Task The_owner_takes_control_when_it_joins_after_someone_else_claimed_it()
    {
        await using var s = new Session();
        var remote = s.Join("remote");
        await s.Send("remote", 1);
        await s.Send("remote", 2);
        Assert.Equal("remote", s.State.PrimaryClientId);

        s.Join("owner", owner: true);

        await WaitFor(() => s.State.PrimaryClientId == "owner");
        Assert.Equal("owner", s.State.OwnerClientId);
        Assert.Equal(1u, s.State.BrowserIPC.SequenceNum); // a hand-off: the new controller starts at 1
        await WaitFor(() => remote.Messages.Contains("primaryChanged:owner"));
    }

    [Fact]
    public async Task An_owner_that_is_already_in_control_keeps_its_message_numbering()
    {
        // The owner's own surface is the /app page: it sends its first messages before its stream opens. Resetting the
        // numbering at that point would strand everything it sends afterwards.
        await using var s = new Session();
        await s.Send("owner", 1);
        await s.Send("owner", 2);
        Assert.Equal(3u, s.State.BrowserIPC.SequenceNum);

        s.Join("owner", owner: true);
        await WaitFor(() => s.State.OwnerControlInitialized);

        Assert.Equal("owner", s.State.PrimaryClientId);
        Assert.Equal(3u, s.State.BrowserIPC.SequenceNum);
        await s.Send("owner", 3);
        Assert.Equal(4u, s.State.BrowserIPC.SequenceNum); // forwarded, not parked
        Assert.Empty(s.State.BrowserIPC.MessageDictionary);
    }

    [Fact]
    public async Task An_owner_that_joins_first_is_the_controller_without_sending_anything()
    {
        await using var s = new Session();
        var owner = s.Join("owner", owner: true);

        await WaitFor(() => s.State.PrimaryClientId == "owner");
        Assert.Equal(1u, s.State.BrowserIPC.SequenceNum);
        await WaitFor(() => owner.Messages.Contains("primaryChanged:owner"));
    }

    [Fact]
    public async Task The_owner_only_takes_control_automatically_once()
    {
        await using var s = new Session();
        var first = s.Join("owner", owner: true);
        s.Join("remote");
        await WaitFor(() => s.State.PrimaryClientId == "owner");
        await s.SetPrimary("owner", "remote");
        Assert.Equal("remote", s.State.PrimaryClientId);

        // The owner's page reconnects (same client id): it must not grab control back on its own.
        first.Leave();
        await WaitFor(() => !s.State.IPC.HasConnection("owner"));
        s.Join("owner", owner: true);
        await WaitFor(() => s.State.IPC.HasConnection("owner"));
        await Task.Delay(200);

        Assert.Equal("remote", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task The_controller_can_give_control_and_the_numbering_restarts()
    {
        await using var s = new Session();
        var owner = s.Join("owner", owner: true);
        var remote = s.Join("remote");
        await WaitFor(() => s.State.PrimaryClientId == "owner");
        await s.Send("owner", 1);
        Assert.Equal(2u, s.State.BrowserIPC.SequenceNum);

        await s.SetPrimary(requester: "owner", target: "remote");

        Assert.Equal("remote", s.State.PrimaryClientId);
        Assert.Equal(1u, s.State.BrowserIPC.SequenceNum);
        await WaitFor(() => owner.Messages.Contains("primaryChanged:remote") && remote.Messages.Contains("primaryChanged:remote"));

        // The previous controller is muted at once; the new one is heard from 1.
        await s.Send("owner", 2);
        Assert.Equal(1u, s.State.BrowserIPC.SequenceNum);
        await s.Send("remote", 1);
        Assert.Equal(2u, s.State.BrowserIPC.SequenceNum);
    }

    [Fact]
    public async Task A_remote_connection_cannot_take_control()
    {
        await using var s = new Session();
        s.Join("owner", owner: true);
        s.Join("remote");
        s.Join("viewer");
        await WaitFor(() => s.State.PrimaryClientId == "owner");

        await s.SetPrimary(requester: "remote", target: "remote");
        Assert.Equal("owner", s.State.PrimaryClientId);

        await s.SetPrimary(requester: "viewer", target: "viewer");
        Assert.Equal("owner", s.State.PrimaryClientId);

        await s.SetPrimary(requester: "viewer", target: "remote"); // nor hand it to someone else
        Assert.Equal("owner", s.State.PrimaryClientId);

        await s.SetPrimary(requester: "nobody", target: "nobody"); // nor can a connection that is not in the session
        Assert.Equal("owner", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task The_owner_can_always_take_control_back_but_only_for_itself()
    {
        await using var s = new Session();
        s.Join("owner", owner: true);
        s.Join("remote");
        s.Join("viewer");
        await WaitFor(() => s.State.PrimaryClientId == "owner");
        await s.SetPrimary("owner", "remote");
        Assert.Equal("remote", s.State.PrimaryClientId);

        await s.SetPrimary(requester: "owner", target: "viewer"); // not the controller now: can't pass it around
        Assert.Equal("remote", s.State.PrimaryClientId);

        await s.SetPrimary(requester: "owner", target: "owner");
        Assert.Equal("owner", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task Control_is_not_given_to_a_connection_that_is_not_attached()
    {
        await using var s = new Session();
        s.Join("owner", owner: true);
        await WaitFor(() => s.State.PrimaryClientId == "owner");

        await s.SetPrimary("owner", "ghost");
        Assert.Equal("owner", s.State.PrimaryClientId);

        await s.SetPrimary("owner", "");
        Assert.Equal("owner", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task SetPrimary_for_an_unknown_session_does_nothing()
    {
        await using var s = new Session();
        await s.Ipc.SetPrimary(new SetPrimaryRequest { Id = Guid.NewGuid().ToString(), ClientId = "a", Target = "a" }, new CallContext(CancellationToken.None));
        Assert.Equal("", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task When_the_controller_leaves_control_returns_to_the_owner()
    {
        await using var s = new Session();
        var owner = s.Join("owner", owner: true);
        var remote = s.Join("remote");
        await WaitFor(() => s.State.PrimaryClientId == "owner");
        await s.SetPrimary("owner", "remote");
        await s.Send("remote", 1);

        remote.Leave(); // the remote user's tab closes while it has control

        await WaitFor(() => s.State.PrimaryClientId == "owner");
        Assert.Equal(1u, s.State.BrowserIPC.SequenceNum);
        Assert.False(s.State.Token.IsCancellationRequested); // the session goes on
        await WaitFor(() => owner.Messages.Contains("primaryChanged:owner"));
        await WaitFor(() => owner.LastRoster() is { } r && r.Select(p => p.Id).SequenceEqual(["owner"]));
    }

    [Fact]
    public async Task A_viewer_leaving_changes_nothing_but_the_roster()
    {
        await using var s = new Session();
        var owner = s.Join("owner", owner: true);
        await WaitFor(() => s.State.PrimaryClientId == "owner");
        var viewer = s.Join("viewer");
        await WaitFor(() => owner.LastRoster()?.Count == 2);

        viewer.Leave();

        await WaitFor(() => owner.LastRoster()?.Count == 1);
        Assert.Equal("owner", s.State.PrimaryClientId);
        Assert.False(s.State.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task The_roster_lists_connections_in_the_order_they_joined_with_their_roles()
    {
        // The first remote connection is the one control is shared with; later ones are read-only viewers. Browsers work
        // that out from the order of this list.
        await using var s = new Session();
        var owner = s.Join("owner", owner: true);
        await WaitFor(() => s.State.PrimaryClientId == "owner");
        var names = new[] { "remote", "viewer-1", "viewer-2", "viewer-3", "viewer-4" };
        foreach (var name in names)
        {
            s.Join(name);
            await WaitFor(() => s.State.IPC.HasConnection(name));
        }

        await WaitFor(() => owner.LastRoster()?.Count == 6);
        var roster = owner.LastRoster()!;
        Assert.Equal(new[] { "owner" }.Concat(names), roster.Select(p => p.Id));
        Assert.Equal(new[] { true, false, false, false, false, false }, roster.Select(p => p.Owner));
        Assert.Equal(new[] { true, false, false, false, false, false }, roster.Select(p => p.Primary));

        await s.SetPrimary("owner", "remote");
        await WaitFor(() => owner.LastRoster() is { } r && r.Single(p => p.Primary).Id == "remote");
        Assert.Equal(new[] { "owner" }.Concat(names), owner.LastRoster()!.Select(p => p.Id)); // order is unchanged by a hand-off
    }

    [Fact]
    public async Task Every_connection_is_sent_the_same_roster()
    {
        await using var s = new Session();
        var owner = s.Join("owner", owner: true);
        await WaitFor(() => s.State.PrimaryClientId == "owner");
        var remote = s.Join("remote");
        await WaitFor(() => s.State.IPC.HasConnection("remote"));
        var viewer = s.Join("viewer");

        foreach (var c in new[] { owner, remote, viewer })
            await WaitFor(() => c.LastRoster() is { Count: 3 } r && r[0].Id == "owner" && r[1].Id == "remote" && r[2].Id == "viewer");
    }

    [Fact]
    public async Task A_client_id_with_quotes_cannot_break_the_roster()
    {
        await using var s = new Session();
        const string odd = "a\"b\\c<d>";
        var c = s.Join(odd, owner: true);

        await WaitFor(() => c.LastRoster()?.Count == 1);
        Assert.Equal(odd, c.LastRoster()![0].Id);
    }

    // ---- helpers ----

    private static async Task WaitFor(Func<bool> condition, int milliseconds = 5000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Timed out waiting for the relay.");
            await Task.Delay(20);
        }
    }

    private sealed record Participant(string Id, bool Owner, bool Primary);

    private sealed class Connection(Task stream, CancellationTokenSource cancel, Writer writer)
    {
        public Task Stream => stream;
        public IReadOnlyCollection<string> Messages => writer.Messages;
        public void Leave() => cancel.Cancel();

        /// <summary>The most recent "participants:" message this browser was sent, or null if none yet.</summary>
        public List<Participant>? LastRoster()
        {
            var last = writer.Messages.LastOrDefault(m => m.StartsWith("participants:", StringComparison.Ordinal));
            if (last is null) return null;
            using var doc = JsonDocument.Parse(last["participants:".Length..]);
            return [.. doc.RootElement.EnumerateArray().Select(e =>
                new Participant(e.GetProperty("id").GetString()!, e.GetProperty("owner").GetBoolean(), e.GetProperty("primary").GetBoolean()))];
        }
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly List<Connection> connections = [];
        public string Id { get; } = Guid.NewGuid().ToString();
        public ServiceState State { get; }
        public BrowserIPCService Ipc { get; }

        public Session()
        {
            var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
            State = new ServiceState(NullLogger<RemoteWebViewService>.Instance, true) { Id = Id };
            var source = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(State);
            services[Id] = source;
            var channels = new ConcurrentDictionary<string, Channel<string>>();
            var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services);
            Ipc = new BrowserIPCService(NullLogger<RemoteWebViewService>.Instance, services, shutdown);
        }

        /// <summary>Records what the relay forwards to the app (browser input that was accepted).</summary>
        public HostWriter CaptureHost()
        {
            var writer = new HostWriter();
            State.IPC.ClientResponseStream = writer;
            return writer;
        }

        /// <summary>A browser opens its stream from the relay (what a page does once, when it starts).</summary>
        public Connection Join(string clientId, bool owner = false)
        {
            var cancel = new CancellationTokenSource();
            var writer = new Writer();
            var stream = Ipc.ReceiveMessage(new ReceiveMessageRequest { Id = Id, ClientId = clientId, IsOwner = owner }, writer, new CallContext(cancel.Token));
            var connection = new Connection(stream, cancel, writer);
            connections.Add(connection);
            return connection;
        }

        /// <summary>A browser sends one message to the app. Every page believes it is the controller until told otherwise.</summary>
        public Task<SendMessageResponse> Send(string clientId, uint sequence) =>
            Ipc.SendMessage(new SendSequenceMessageRequest { Id = Id, ClientId = clientId, Sequence = sequence, IsPrimary = true, Message = $"m{sequence}-{clientId}" },
                new CallContext(CancellationToken.None));

        public Task SetPrimary(string requester, string target) =>
            Ipc.SetPrimary(new SetPrimaryRequest { Id = Id, ClientId = requester, Target = target }, new CallContext(CancellationToken.None));

        public async ValueTask DisposeAsync()
        {
            foreach (var c in connections) c.Leave();
            try { await Task.WhenAll(connections.Select(c => c.Stream)).WaitAsync(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
        }
    }

    private sealed class Writer : IServerStreamWriter<StringRequest>
    {
        private readonly ConcurrentQueue<string> messages = new();
        public IReadOnlyCollection<string> Messages => messages;
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(StringRequest message) { messages.Enqueue(message.Request); return Task.CompletedTask; }
        // The relay writes with a token; the interface's default for this overload throws when the token can be cancelled.
        public Task WriteAsync(StringRequest message, CancellationToken cancellationToken) => WriteAsync(message);
    }

    private sealed class HostWriter : IServerStreamWriter<WebMessageResponse>
    {
        private readonly ConcurrentQueue<string> messages = new();
        public IReadOnlyCollection<string> Messages => messages;
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(WebMessageResponse message) { messages.Enqueue(message.Response); return Task.CompletedTask; }
        public Task WriteAsync(WebMessageResponse message, CancellationToken cancellationToken) => WriteAsync(message);
    }

    private sealed class CallContext(CancellationToken token) : ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => token;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
