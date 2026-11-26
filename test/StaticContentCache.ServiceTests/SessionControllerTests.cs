using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Agent;
using PeakSWC.RemoteWebView.Agents;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

/// <summary>
/// A session started through AgentAdmin for a signed-in user gives control only to that user. Other members of the group
/// can watch, but neither claim control first nor be handed it by the page at the computer. The user in control can still
/// hand it to someone on purpose.
/// </summary>
public class SessionControllerTests
{
    private const string Shop = "shop-a";

    [Fact]
    public void StartSession_remembers_the_signed_in_requester_by_session_id()
    {
        var registry = new AgentRegistry();
        registry.Register(new AgentHello { MachineId = "m-1", MachineName = "PC-1", AgentVersion = "1.0.0", Lifetime = Lifetime.Persistent, ConsentCeiling = ConsentMode.ConsentUnattendedAllowed }, "10.0.0.1", null);

        var started = registry.StartSession("m-1", true, "Pat", "https://relay.example", true, true, requesterUserId: "pat").Response;

        Assert.True(started.Accepted);
        Assert.Equal("pat", registry.StartedBy(started.SessionId));
        Assert.Null(registry.StartedBy("some-other-session"));
    }

    [Fact]
    public void StartSession_without_sign_in_names_nobody()
    {
        var registry = new AgentRegistry();
        registry.Register(new AgentHello { MachineId = "m-1", MachineName = "PC-1", AgentVersion = "1.0.0", Lifetime = Lifetime.Persistent, ConsentCeiling = ConsentMode.ConsentUnattendedAllowed }, "10.0.0.1", null);

        var started = registry.StartSession("m-1", true, "Pat", "https://relay.example", true, true).Response;

        Assert.Null(registry.StartedBy(started.SessionId));
    }

    [Fact]
    public async Task Another_member_cannot_claim_control_first()
    {
        using var s = new Relay(startedBy: "pat");

        var response = await s.Browser.SendMessage(Claim(s.Id, "eve"), new CallContext(http: s.SignedIn("eve")));

        Assert.False(response.Success);
        Assert.Equal("", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task The_requester_can_claim_control_first()
    {
        using var s = new Relay(startedBy: "pat");

        var response = await s.Browser.SendMessage(Claim(s.Id, "pat"), new CallContext(http: s.SignedIn("pat")));

        Assert.True(response.Success);
        Assert.Equal("pat", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task Without_a_requester_any_member_can_claim_control_as_before()
    {
        using var s = new Relay(startedBy: null);

        var response = await s.Browser.SendMessage(Claim(s.Id, "eve"), new CallContext(http: s.SignedIn("eve")));

        Assert.True(response.Success);
        Assert.Equal("eve", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task The_owner_page_cannot_hand_control_to_a_member_who_is_only_watching()
    {
        using var s = new Relay(startedBy: "pat");
        await s.Join("owner", owner: true);
        await s.Join("eve");

        await s.Browser.SetPrimary(new SetPrimaryRequest { Id = s.Id, ClientId = "owner", ClientKey = "k-owner", Target = "eve" }, new CallContext());

        Assert.Equal("owner", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task The_owner_page_hands_control_to_the_requester()
    {
        using var s = new Relay(startedBy: "pat");
        await s.Join("owner", owner: true);
        await s.Join("eve");
        await s.Join("pat");

        await s.Browser.SetPrimary(new SetPrimaryRequest { Id = s.Id, ClientId = "owner", ClientKey = "k-owner", Target = "pat" }, new CallContext());

        Assert.Equal("pat", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task The_requester_can_hand_control_to_a_colleague_who_then_counts_as_a_controller()
    {
        using var s = new Relay(startedBy: "pat");
        await s.Join("owner", owner: true);
        await s.Join("pat");
        await s.Join("eve");
        await s.Browser.SetPrimary(new SetPrimaryRequest { Id = s.Id, ClientId = "owner", ClientKey = "k-owner", Target = "pat" }, new CallContext());

        await s.Browser.SetPrimary(new SetPrimaryRequest { Id = s.Id, ClientId = "pat", ClientKey = "k-pat", Target = "eve" }, new CallContext(http: s.SignedIn("pat")));
        Assert.Equal("eve", s.State.PrimaryClientId);

        // Back to the owner, who may now give it to the colleague again.
        await s.Browser.SetPrimary(new SetPrimaryRequest { Id = s.Id, ClientId = "owner", ClientKey = "k-owner", Target = "owner" }, new CallContext());
        Assert.Equal("owner", s.State.PrimaryClientId);
        await s.Browser.SetPrimary(new SetPrimaryRequest { Id = s.Id, ClientId = "owner", ClientKey = "k-owner", Target = "eve" }, new CallContext());
        Assert.Equal("eve", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task The_roster_lists_the_requester_before_members_who_joined_earlier()
    {
        using var s = new Relay(startedBy: "pat");
        await s.Join("owner", owner: true);
        await s.Join("eve");
        await s.Join("pat");

        var roster = s.Roster("owner");

        Assert.True(roster.IndexOf("\"id\":\"pat\"", StringComparison.Ordinal) < roster.IndexOf("\"id\":\"eve\"", StringComparison.Ordinal), roster);
    }

    [Fact]
    public async Task Without_a_requester_the_roster_keeps_join_order()
    {
        using var s = new Relay(startedBy: null);
        await s.Join("owner", owner: true);
        await s.Join("eve");
        await s.Join("pat");

        var roster = s.Roster("owner");

        Assert.True(roster.IndexOf("\"id\":\"eve\"", StringComparison.Ordinal) < roster.IndexOf("\"id\":\"pat\"", StringComparison.Ordinal), roster);
    }

    [Fact]
    public async Task CreateWebView_takes_the_requester_from_the_agent_launch()
    {
        var registry = new AgentRegistry();
        registry.Register(new AgentHello { MachineId = "m-1", MachineName = "PC-1", AgentVersion = "1.0.0", Lifetime = Lifetime.Persistent, ConsentCeiling = ConsentMode.ConsentUnattendedAllowed }, "10.0.0.1", null);
        var started = registry.StartSession("m-1", true, "Pat", "https://relay.example", true, true, requesterUserId: "pat").Response;

        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services);
        var app = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, services, channels, shutdown,
            new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance), agents: registry);
        using var cancel = new CancellationTokenSource();

        var call = app.CreateWebView(new CreateWebViewRequest { Id = started.SessionId, HtmlHostPath = "wwwroot" }, new AppWriter(), new CallContext(token: cancel.Token));
        var state = await services.GetOrAdd(started.SessionId, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(state.Controllers.IsLimited);
        Assert.True(state.Controllers.Allows("pat"));
        Assert.False(state.Controllers.Allows("eve"));

        cancel.Cancel();
        await state.DisposeAsync();
        try { await call.WaitAsync(TimeSpan.FromSeconds(5)); } catch (Exception) { /* the call ends with the session */ }
    }

    // ---- helpers ----

    private static SendSequenceMessageRequest Claim(string id, string clientId) =>
        new() { Id = id, ClientId = clientId, ClientKey = "k-" + clientId, IsPrimary = true, Sequence = 1, Message = "m1" };

    private sealed class Relay : IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> services = new();
        private readonly FakeUsers users = new();
        private readonly CancellationTokenSource cancel = new();
        private readonly List<Task> streams = [];
        private readonly Dictionary<string, RecordingWriter> writers = [];

        public string Id { get; } = Guid.NewGuid().ToString();
        public ServiceState State { get; }
        public BrowserIPCService Browser { get; }

        public Relay(string? startedBy)
        {
            var channels = new ConcurrentDictionary<string, Channel<string>>();
            var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services);
            State = new ServiceState(NullLogger<RemoteWebViewService>.Instance, true) { Id = Id, Group = Shop, OwnerKey = "app-secret" };
            State.Controllers.Add(startedBy);
            var source = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(State);
            services[Id] = source;
            Browser = new BrowserIPCService(NullLogger<RemoteWebViewService>.Instance, services, shutdown, new FakeAuthorization(), new FakeUserService(users));
        }

        public DefaultHttpContext SignedIn(string name)
        {
            users.Set(name, [Shop]);
            var identity = new ClaimsIdentity([new Claim(FakeAuthorization.ObjectIdClaim, name), new Claim("name", name)], FakeAuthorization.Scheme);
            return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        }

        /// <summary>Opens a browser stream: the owner's page (with the owner token) or a signed-in member named like its clientId.</summary>
        public async Task Join(string clientId, bool owner = false)
        {
            var writer = new RecordingWriter();
            writers[clientId] = writer;
            var request = new ReceiveMessageRequest { Id = Id, ClientId = clientId, ClientKey = "k-" + clientId, IsOwner = owner, OwnerToken = owner ? State.OwnerToken : "" };
            var http = owner ? new DefaultHttpContext() : SignedIn(clientId);
            streams.Add(Browser.ReceiveMessage(request, writer, new CallContext(http, cancel.Token)));
            await WaitFor(() => State.IPC.HasConnection(clientId) && (!owner || State.OwnerClientId == clientId));
        }

        /// <summary>The newest roster the given browser was sent.</summary>
        public string Roster(string clientId)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var last = writers[clientId].Messages.LastOrDefault(m => m.StartsWith("participants:", StringComparison.Ordinal));
                if (last != null && State.IPC.ConnectionUserId("pat") != null && last.Contains("\"pat\"", StringComparison.Ordinal) && last.Contains("\"eve\"", StringComparison.Ordinal))
                    return last;
                Thread.Sleep(10);
            }
            throw new TimeoutException("no full roster");
        }

        public void Dispose()
        {
            cancel.Cancel();
            try { Task.WhenAll(streams).Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
            State.DisposeAsync().AsTask().GetAwaiter().GetResult();
            cancel.Dispose();
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > until)
                throw new TimeoutException("condition not met");
            await Task.Delay(10);
        }
    }

    private sealed class RecordingWriter : IServerStreamWriter<StringRequest>
    {
        private readonly ConcurrentQueue<string> messages = new();
        public IReadOnlyList<string> Messages => messages.ToArray();
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(StringRequest message) { messages.Enqueue(message.Request); return Task.CompletedTask; }
        public Task WriteAsync(StringRequest message, CancellationToken cancellationToken) => WriteAsync(message);
    }

    private sealed class AppWriter : IServerStreamWriter<WebMessageResponse>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(WebMessageResponse message) => Task.CompletedTask;
        public Task WriteAsync(WebMessageResponse message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CallContext(HttpContext? http = null, CancellationToken token = default) : ServerCallContext
    {
        private readonly Dictionary<object, object> userState = http != null ? new() { ["__HttpContext"] = http } : new();

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
        protected override IDictionary<object, object> UserStateCore => userState;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
