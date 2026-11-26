using System.Collections.Concurrent;
using System.Security.Claims;
using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Agents;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

/// <summary>
/// With sign-in on, the gRPC calls behind a session's pages follow the pages' own rule: the browser calls need the owner
/// or a member of the session's group, the relay-wide calls need an operator (or show only the caller's shop), and the
/// app's calls need the app's own key. Without sign-in nothing changes.
/// </summary>
public class SessionCallAccessTests
{
    private const string Shop = "shop-a";

    // ---- Browser calls (BrowserIPC) ----

    [Fact]
    public async Task A_browser_that_is_not_signed_in_cannot_open_a_session_stream()
    {
        using var s = new Relay(new FakeAuthorization());

        var error = await Assert.ThrowsAsync<RpcException>(() => s.Browser.ReceiveMessage(
            new ReceiveMessageRequest { Id = s.Id, ClientId = "x", ClientKey = "kx" }, new NullWriter(), new CallContext(http: Anonymous())));

        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.False(s.State.IPC.HasConnection("x"));
    }

    [Fact]
    public async Task A_signed_in_user_from_another_shop_cannot_open_a_session_stream()
    {
        using var s = new Relay(new FakeAuthorization());

        var error = await Assert.ThrowsAsync<RpcException>(() => s.Browser.ReceiveMessage(
            new ReceiveMessageRequest { Id = s.Id, ClientId = "x", ClientKey = "kx" }, new NullWriter(), new CallContext(http: s.SignedIn("eve", "shop-b"))));

        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
    }

    [Fact]
    public async Task A_member_of_the_session_group_can_open_a_session_stream()
    {
        using var s = new Relay(new FakeAuthorization());
        using var cancel = new CancellationTokenSource();

        var stream = s.Browser.ReceiveMessage(new ReceiveMessageRequest { Id = s.Id, ClientId = "tech", ClientKey = "kt" }, new NullWriter(),
            new CallContext(cancel.Token, s.SignedIn("tech", Shop)));

        await WaitFor(() => s.State.IPC.HasConnection("tech"));
        Assert.True(s.State.IPC.HasConnection("tech"));
        cancel.Cancel();
        await stream.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task The_owner_page_with_the_owner_token_needs_no_sign_in()
    {
        using var s = new Relay(new FakeAuthorization(), ownerKey: "app-secret");
        using var cancel = new CancellationTokenSource();

        var stream = s.Browser.ReceiveMessage(new ReceiveMessageRequest { Id = s.Id, ClientId = "owner", ClientKey = "ko", IsOwner = true, OwnerToken = s.State.OwnerToken },
            new NullWriter(), new CallContext(cancel.Token, Anonymous()));

        await WaitFor(() => s.State.OwnerClientId == "owner");
        Assert.Equal("owner", s.State.OwnerClientId);
        cancel.Cancel();
        await stream.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task A_browser_that_is_not_signed_in_cannot_claim_control()
    {
        using var s = new Relay(new FakeAuthorization());

        var response = await s.Browser.SendMessage(new SendSequenceMessageRequest { Id = s.Id, ClientId = "x", ClientKey = "kx", IsPrimary = true, Sequence = 1, Message = "connected:" },
            new CallContext(http: Anonymous()));

        Assert.False(response.Success);
        Assert.Equal("", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task A_member_of_the_session_group_can_claim_control()
    {
        using var s = new Relay(new FakeAuthorization());

        var response = await s.Browser.SendMessage(new SendSequenceMessageRequest { Id = s.Id, ClientId = "tech", ClientKey = "kt", IsPrimary = true, Sequence = 1, Message = "m1" },
            new CallContext(http: s.SignedIn("tech", Shop)));

        Assert.True(response.Success);
        Assert.Equal("tech", s.State.PrimaryClientId);
    }

    [Fact]
    public async Task Without_sign_in_on_the_relay_browser_calls_work_as_before()
    {
        using var s = new Relay(new NoAuthorization());

        var response = await s.Browser.SendMessage(new SendSequenceMessageRequest { Id = s.Id, ClientId = "a", ClientKey = "ka", IsPrimary = true, Sequence = 1, Message = "m1" },
            new CallContext());

        Assert.True(response.Success);
        Assert.Equal("a", s.State.PrimaryClientId);
    }

    // ---- Relay-wide calls (ClientIPC) ----

    [Fact]
    public async Task Server_status_is_refused_to_a_caller_who_is_not_signed_in()
    {
        using var s = new Relay(new FakeAuthorization());

        var error = await Assert.ThrowsAsync<RpcException>(() => s.Clients().GetServerStatus(new Empty(), new CallContext(http: Anonymous())));

        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
    }

    [Fact]
    public async Task Server_status_shows_a_shop_only_its_own_sessions()
    {
        using var s = new Relay(new FakeAuthorization());
        var other = s.AddSession("shop-b");

        var status = await s.Clients().GetServerStatus(new Empty(), new CallContext(http: s.SignedIn("tech", Shop)));

        Assert.Equal([s.Id], status.ConnectionResponses.Select(c => c.Id));
        Assert.DoesNotContain(status.ConnectionResponses, c => c.Id == other);
    }

    [Fact]
    public async Task Server_status_shows_an_operator_every_session()
    {
        using var s = new Relay(new FakeAuthorization());
        var other = s.AddSession("shop-b");

        var status = await s.Clients(adminKey: "operator-key").GetServerStatus(new Empty(), new CallContext(http: Anonymous(), adminKey: "operator-key"));

        Assert.Equal(new[] { s.Id, other }.Order(), status.ConnectionResponses.Select(c => c.Id).Order());
    }

    [Fact]
    public async Task Server_status_shows_every_session_on_a_relay_without_sign_in()
    {
        using var s = new Relay(new NoAuthorization());
        var other = s.AddSession("shop-b");

        var status = await s.Clients().GetServerStatus(new Empty(), new CallContext());

        Assert.Equal(2, status.ConnectionResponses.Count);
        Assert.Contains(status.ConnectionResponses, c => c.Id == other);
    }

    [Fact]
    public async Task The_relay_log_and_the_cache_switches_are_for_operators_only()
    {
        using var s = new Relay(new FakeAuthorization());
        var clients = s.Clients(adminKey: "operator-key");
        var shopUser = new CallContext(http: s.SignedIn("tech", Shop));

        Assert.Equal(StatusCode.PermissionDenied, (await Assert.ThrowsAsync<RpcException>(() => clients.GetLoggedEvents(new Empty(), shopUser))).StatusCode);
        Assert.Equal(StatusCode.PermissionDenied, (await Assert.ThrowsAsync<RpcException>(() => clients.SetCache(new CacheRequest { EnableServerCache = false }, shopUser))).StatusCode);
        Assert.Equal(StatusCode.PermissionDenied, (await Assert.ThrowsAsync<RpcException>(() =>
            clients.SetCache(new CacheRequest { EnableServerCache = false }, new CallContext(http: Anonymous(), adminKey: "wrong")))).StatusCode);

        await clients.GetLoggedEvents(new Empty(), new CallContext(http: Anonymous(), adminKey: "operator-key"));
        await clients.SetCache(new CacheRequest { EnableServerCache = false }, new CallContext(http: Anonymous(), adminKey: "operator-key"));
        Assert.False(s.Files.UseServerCache);
    }

    // ---- The app's calls (WebViewIPC) ----

    [Fact]
    public async Task Only_the_app_with_its_key_can_send_to_or_end_its_session()
    {
        using var s = new Relay(new NoAuthorization(), appKey: "app-key");
        var app = s.App();

        Assert.False((await app.SendMessage(new SendMessageRequest { Id = s.Id, Message = "x" }, new CallContext())).Success);
        Assert.False((await app.SendMessage(new SendMessageRequest { Id = s.Id, Message = "x" }, new CallContext(appKey: "guess"))).Success);
        var error = await Assert.ThrowsAsync<RpcException>(() => app.Shutdown(new IdMessageRequest { Id = s.Id }, new CallContext(appKey: "guess")));
        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.True(s.IsRegistered);

        await app.Shutdown(new IdMessageRequest { Id = s.Id }, new CallContext(appKey: "app-key"));
        await WaitFor(() => !s.IsRegistered);
    }

    [Fact]
    public async Task A_session_from_an_app_without_a_key_takes_the_id_alone()
    {
        using var s = new Relay(new NoAuthorization());

        await s.App().Shutdown(new IdMessageRequest { Id = s.Id }, new CallContext());

        await WaitFor(() => !s.IsRegistered);
        Assert.False(s.IsRegistered);
    }

    // ---- helpers ----

    private static HttpContext Anonymous() => new DefaultHttpContext();

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

    private sealed class Relay : IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> services = new();
        private readonly ConcurrentDictionary<string, Channel<string>> channels = new();
        private readonly ShutdownService shutdown;
        private readonly FakeUsers users = new();
        private readonly IRelayAuthorization authorization;

        public string Id { get; } = Guid.NewGuid().ToString();
        public ServiceState State { get; }
        public BrowserIPCService Browser { get; }
        public RemoteFilesOptions Files { get; } = new();
        public bool IsRegistered => services.ContainsKey(Id);

        public Relay(IRelayAuthorization authorization, string ownerKey = "", string appKey = "")
        {
            this.authorization = authorization;
            shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services);
            State = Add(Id, Shop, ownerKey, appKey);
            Browser = new BrowserIPCService(NullLogger<RemoteWebViewService>.Instance, services, shutdown, authorization, new FakeUserService(users));
        }

        public string AddSession(string group)
        {
            var id = Guid.NewGuid().ToString();
            Add(id, group, "", "");
            return id;
        }

        private ServiceState Add(string id, string group, string ownerKey, string appKey)
        {
            var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, true) { Id = id, Group = group, OwnerKey = ownerKey, AppKey = appKey };
            var source = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(state);
            services[id] = source;
            return state;
        }

        /// <summary>A signed-in user in the given groups, as the sign-in middleware would leave the request.</summary>
        public HttpContext SignedIn(string name, params string[] groups)
        {
            users.Set(name, groups);
            var identity = new ClaimsIdentity([new Claim(FakeAuthorization.ObjectIdClaim, name), new Claim("name", name)], FakeAuthorization.Scheme);
            return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        }

        public ClientIPCService Clients(string? adminKey = null) =>
            new(NullLogger<ClientIPCService>.Instance, channels, services, new FakeUserService(users), Files,
                authorization, new AllowAllAgentTenantPolicy(), AgentOptions.Create(null, null, adminKey));

        public RemoteWebViewService App() =>
            new(NullLogger<RemoteWebViewService>.Instance, services, channels, shutdown, new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance), authorization: authorization);

        public void Dispose()
        {
            foreach (var source in services.Values)
                if (source.Task.IsCompletedSuccessfully)
                    source.Task.Result.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed class NullWriter : IServerStreamWriter<StringRequest>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(StringRequest message) => Task.CompletedTask;
        public Task WriteAsync(StringRequest message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CallContext : ServerCallContext
    {
        private readonly CancellationToken token;
        private readonly Dictionary<object, object> userState = new();

        public CallContext(CancellationToken token = default, HttpContext? http = null, string? adminKey = null, string? appKey = null)
        {
            this.token = token;
            if (http != null)
                userState["__HttpContext"] = http;
            if (adminKey != null)
                RequestHeadersCore.Add(AgentOptions.AdminKeyHeader, adminKey);
            if (appKey != null)
                RequestHeadersCore.Add(AppAccess.HeaderName, appKey);
        }

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
