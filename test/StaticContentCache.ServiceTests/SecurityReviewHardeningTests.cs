using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Agents;
using PeakSWC.RemoteWebView.EndPoints;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

/// <summary>
/// Regression tests for the remaining findings in the RemoteBlazorWebView security review:
/// F1 (FileWatcherService auth/confinement/dev gating), F5 (cookie filtering), F6 (RemoteFilesMiddleware auth +
/// ServiceDictionary unbounded GetOrAdd DoS), F8 (CORS AllowedOrigins), and F9 (pre-session TOCTOU + bounded user cache).
/// </summary>
public class SecurityReviewHardeningTests
{
    // ---- F1: FileWatcherService ----

    [Fact]
    public async Task FileWatcherService_refuses_non_operator_when_sign_in_is_enabled()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FileWatcher:AllowedRoot"] = Path.GetTempPath() })
                .Build();
            var service = new FileWatcherService(
                NullLogger<FileWatcherService>.Instance,
                authorization: new FakeAuthorization(),
                tenantPolicy: new AllowAllAgentTenantPolicy(),
                agentOptions: new AgentOptions { AdminKey = "secret-admin-key" },
                userService: new FakeUserService(new FakeUsers()),
                configuration: config);

            var error = await Assert.ThrowsAsync<RpcException>(() =>
                service.WatchFile(new WatchFileRequest { FilePath = tempFile }, new NullWatchWriter(), new TestCallContext(http: AnonymousHttp())));

            Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FileWatcherService_refuses_when_sign_in_is_enabled_and_AllowedRoot_is_not_configured()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var service = new FileWatcherService(
                NullLogger<FileWatcherService>.Instance,
                authorization: new FakeAuthorization(),
                tenantPolicy: new AllowAllAgentTenantPolicy(),
                agentOptions: new AgentOptions { AdminKey = "secret-admin-key" },
                userService: new FakeUserService(new FakeUsers()),
                configuration: new ConfigurationBuilder().Build());

            var headers = new Metadata { { AgentOptions.AdminKeyHeader, "secret-admin-key" } };
            var error = await Assert.ThrowsAsync<RpcException>(() =>
                service.WatchFile(new WatchFileRequest { FilePath = tempFile }, new NullWatchWriter(), new TestCallContext(http: AnonymousHttp(), headers: headers)));

            Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task FileWatcherService_rejects_path_traversal_outside_AllowedRoot()
    {
        var allowedDir = Path.Combine(Path.GetTempPath(), "rbwv-fw-allowed-" + Guid.NewGuid().ToString("N"));
        var outsideFile = Path.Combine(Path.GetTempPath(), "rbwv-fw-outside-" + Guid.NewGuid().ToString("N") + ".txt");
        Directory.CreateDirectory(allowedDir);
        await File.WriteAllTextAsync(outsideFile, "secret");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FileWatcher:AllowedRoot"] = allowedDir })
                .Build();
            var service = new FileWatcherService(
                NullLogger<FileWatcherService>.Instance,
                authorization: new FakeAuthorization(),
                tenantPolicy: new AllowAllAgentTenantPolicy(),
                agentOptions: new AgentOptions { AdminKey = "secret-admin-key" },
                userService: new FakeUserService(new FakeUsers()),
                configuration: config);

            var traversalPath = Path.Combine(allowedDir, "..", Path.GetFileName(outsideFile));
            var headers = new Metadata { { AgentOptions.AdminKeyHeader, "secret-admin-key" } };
            var error = await Assert.ThrowsAsync<RpcException>(() =>
                service.WatchFile(new WatchFileRequest { FilePath = traversalPath }, new NullWatchWriter(), new TestCallContext(http: AnonymousHttp(), headers: headers)));

            Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        }
        finally
        {
            if (File.Exists(outsideFile)) File.Delete(outsideFile);
            if (Directory.Exists(allowedDir)) Directory.Delete(allowedDir, recursive: true);
        }
    }

    [Fact]
    public async Task FileWatcherService_allows_operator_inside_AllowedRoot()
    {
        var allowedDir = Path.Combine(Path.GetTempPath(), "rbwv-fw-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(allowedDir);
        var insideFile = Path.Combine(allowedDir, "hello.txt");
        await File.WriteAllTextAsync(insideFile, "hello");
        try
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["FileWatcher:AllowedRoot"] = allowedDir })
                .Build();
            var service = new FileWatcherService(
                NullLogger<FileWatcherService>.Instance,
                authorization: new FakeAuthorization(),
                tenantPolicy: new AllowAllAgentTenantPolicy(),
                agentOptions: new AgentOptions { AdminKey = "secret-admin-key" },
                userService: new FakeUserService(new FakeUsers()),
                configuration: config);

            using var cts = new CancellationTokenSource();
            var chunks = new List<WatchFileResponse>();
            var writer = new CallbackWatchWriter(msg =>
            {
                chunks.Add(msg);
                if (msg.ResponseCase == WatchFileResponse.ResponseOneofCase.Chunk && msg.Chunk.Content.IsEmpty)
                    cts.Cancel();
            });
            var headers = new Metadata { { AgentOptions.AdminKeyHeader, "secret-admin-key" } };

            await service.WatchFile(new WatchFileRequest { FilePath = insideFile }, writer, new TestCallContext(cts.Token, AnonymousHttp(), headers));

            Assert.Contains(chunks, c => c.ResponseCase == WatchFileResponse.ResponseOneofCase.Chunk && c.Chunk.Content.ToStringUtf8() == "hello");
        }
        finally
        {
            if (Directory.Exists(allowedDir)) Directory.Delete(allowedDir, recursive: true);
        }
    }

    // ---- F5: RelayCookieFilter ----

    [Fact]
    public void RelayCookieFilter_strips_auth_owner_voice_and_framework_cookies()
    {
        var cookies = new FakeRequestCookieCollection(new Dictionary<string, string>
        {
            ["rwv-relay-ticket"] = "signed-ticket",
            ["rbwv-owner-1234"] = "owner-secret",
            ["rbwv-voice-1234"] = "voice-secret",
            ["rbwv-test-user"] = "alice",
            [".AspNetCore.Cookies"] = "aspnet-auth",
            [".AspNetCore.Antiforgery.abc"] = "xsrf",
            ["__Host-session"] = "host-secret",
            ["__Secure-token"] = "secure-secret",
            ["theme"] = "dark",
            ["locale"] = "en-US",
        });

        var filtered = RelayCookieFilter.FilterForClient(cookies);

        Assert.Equal(2, filtered.Count);
        Assert.Equal("dark", filtered["theme"]);
        Assert.Equal("en-US", filtered["locale"]);
        Assert.DoesNotContain("rwv-relay-ticket", filtered.Keys);
        Assert.DoesNotContain(".AspNetCore.Cookies", filtered.Keys);
    }

    // ---- F6: RemoteFilesMiddleware auth & ServiceDictionary DoS ----

    [Fact]
    public async Task RemoteFilesMiddleware_returns_404_for_unknown_guid_without_inserting_into_ServiceDictionary()
    {
        var sessions = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var syncManager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var resolver = new RemoteFileResolver(NullLogger<RemoteFileResolver>.Instance, syncManager);
        var middleware = new RemoteFilesMiddleware(_ => Task.CompletedTask, cache, NullLogger<RemoteFilesMiddleware>.Instance, resolver, syncManager);

        var services = new ServiceCollection()
            .AddSingleton(new RemoteFilesOptions())
            .AddSingleton(sessions)
            .AddSingleton<ILogger<RemoteWebViewService>>(NullLogger<RemoteWebViewService>.Instance)
            .BuildServiceProvider();

        var randomGuid = Guid.NewGuid();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/{randomGuid}/app.css";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(sessions);
    }

    [Fact]
    public async Task RemoteFilesMiddleware_refuses_unauthenticated_and_wrong_shop_callers_when_sign_in_is_enabled()
    {
        var sessionId = Guid.NewGuid();
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: false) { Id = sessionId.ToString(), Group = "shop-a" };
        var tcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(state);
        var sessions = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>
        {
            [sessionId.ToString()] = tcs
        };

        var userService = new StaticUserService(new Dictionary<string, string[]> { ["eve"] = ["shop-b"] });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var syncManager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var resolver = new RemoteFileResolver(NullLogger<RemoteFileResolver>.Instance, syncManager);
        var middleware = new RemoteFilesMiddleware(_ => Task.CompletedTask, cache, NullLogger<RemoteFilesMiddleware>.Instance, resolver, syncManager);

        var services = new ServiceCollection()
            .AddSingleton(new RemoteFilesOptions())
            .AddSingleton(sessions)
            .AddSingleton<IRelayAuthorization>(new FakeAuthorization())
            .AddSingleton<IUserService>(userService)
            .AddSingleton<ILogger<RemoteWebViewService>>(NullLogger<RemoteWebViewService>.Instance)
            .BuildServiceProvider();

        // Anonymous caller -> 401
        var anonCtx = new DefaultHttpContext { RequestServices = services };
        anonCtx.Request.Method = HttpMethods.Get;
        anonCtx.Request.Path = $"/{sessionId}/app.css";
        await middleware.InvokeAsync(anonCtx);
        Assert.Equal(StatusCodes.Status401Unauthorized, anonCtx.Response.StatusCode);

        // Signed-in caller from another shop -> 403
        var otherShopCtx = new DefaultHttpContext { RequestServices = services, User = SignedInPrincipal("eve") };
        otherShopCtx.Request.Method = HttpMethods.Get;
        otherShopCtx.Request.Path = $"/{sessionId}/app.css";
        await middleware.InvokeAsync(otherShopCtx);
        Assert.Equal(StatusCodes.Status403Forbidden, otherShopCtx.Response.StatusCode);
    }

    [Fact]
    public async Task BrowserIPC_rejects_unauthenticated_unknown_session_without_growing_ServiceDictionary()
    {
        var sessions = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, sessions);
        var browser = new BrowserIPCService(NullLogger<RemoteWebViewService>.Instance, sessions, shutdown, new FakeAuthorization(), new FakeUserService(new FakeUsers()));

        var randomId = Guid.NewGuid().ToString();
        var error = await Assert.ThrowsAsync<RpcException>(() => browser.ReceiveMessage(
            new ReceiveMessageRequest { Id = randomId, ClientId = "c1", ClientKey = "k1" },
            new NullStringWriter(),
            new TestCallContext(http: AnonymousHttp())));

        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
        Assert.Empty(sessions);

        var sendResult = await browser.SendMessage(
            new SendSequenceMessageRequest { Id = randomId, ClientId = "c1", ClientKey = "k1", IsPrimary = true, Sequence = 1, Message = "m" },
            new TestCallContext(http: AnonymousHttp()));

        Assert.False(sendResult.Success);
        Assert.Empty(sessions);
    }

    // ---- F8: CORS AllowedOrigins ----

    [Fact]
    public async Task Startup_restricts_CORS_when_AllowedOrigins_is_configured()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.UseEnvironment(Environments.Production);
                web.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [RemoteWebViewServer.EmbeddedKey] = "true",
                    [RemoteWebViewServer.HttpsRedirectionKey] = "false",
                    ["Cors:AllowedOrigins"] = "https://admin.example.com"
                }));
                web.UseStartup<Startup>();
            })
            .StartAsync();

        var client = host.GetTestClient();

        using var allowedReq = new HttpRequestMessage(HttpMethod.Get, "/health");
        allowedReq.Headers.Add("Origin", "https://admin.example.com");
        using var allowedResp = await client.SendAsync(allowedReq);
        Assert.True(allowedResp.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedValues));
        Assert.Contains("https://admin.example.com", allowedValues);

        using var blockedReq = new HttpRequestMessage(HttpMethod.Get, "/health");
        blockedReq.Headers.Add("Origin", "https://evil.example.net");
        using var blockedResp = await client.SendAsync(blockedReq);
        Assert.False(blockedResp.Headers.Contains("Access-Control-Allow-Origin"));
    }

    // ---- F9: Post-wait TOCTOU group re-check in Start endpoint ----

    [Fact]
    public async Task Start_rechecks_group_membership_after_waiting_for_pre_opened_session()
    {
        var sessionId = Guid.NewGuid();
        var sessions = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var userService = new StaticUserService(new Dictionary<string, string[]> { ["eve"] = ["shop-b"] });

        var services = new ServiceCollection()
            .AddSingleton(sessions)
            .AddSingleton(channels)
            .AddSingleton<IRelayAuthorization>(new FakeAuthorization())
            .AddSingleton<IUserService>(userService)
            .AddSingleton<ILogger<RemoteWebViewService>>(NullLogger<RemoteWebViewService>.Instance)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services, User = SignedInPrincipal("eve") };
        context.Request.Method = HttpMethods.Get;
        context.Request.RouteValues["id"] = sessionId.ToString();
        context.Response.Body = new MemoryStream();

        var startTask = Endpoints.Start()(context);

        // Wait briefly for Start to insert its pending TCS, then resolve the session for shop-a.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!sessions.ContainsKey(sessionId.ToString()) && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(sessions.TryGetValue(sessionId.ToString(), out var tcs));
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: false) { Id = sessionId.ToString(), Group = "shop-a" };
        tcs.SetResult(state);

        await startTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(state.InUse);
    }

    // ---- Helpers ----

    private static HttpContext AnonymousHttp() => new DefaultHttpContext();

    private static ClaimsPrincipal SignedInPrincipal(string oid) =>
        new(new ClaimsIdentity([new Claim("oid", oid), new Claim("name", oid)], "test"));

    private sealed class StaticUserService(Dictionary<string, string[]> map) : IUserService
    {
        public Task<IReadOnlyList<string>> GetUserGroups(string oid) =>
            Task.FromResult<IReadOnlyList<string>>(map.TryGetValue(oid, out var g) ? g : []);
    }

    private sealed class FakeRequestCookieCollection(Dictionary<string, string> dict) : IRequestCookieCollection
    {
        public string? this[string key] => dict.TryGetValue(key, out var v) ? v : null;
        public int Count => dict.Count;
        public ICollection<string> Keys => dict.Keys;
        public bool ContainsKey(string key) => dict.ContainsKey(key);
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => dict.GetEnumerator();
        public bool TryGetValue(string key, out string value) => dict.TryGetValue(key, out value!);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => dict.GetEnumerator();
    }

    private sealed class NullWatchWriter : IServerStreamWriter<WatchFileResponse>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(WatchFileResponse message) => Task.CompletedTask;
        public Task WriteAsync(WatchFileResponse message, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CallbackWatchWriter(Action<WatchFileResponse> onWrite) : IServerStreamWriter<WatchFileResponse>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(WatchFileResponse message) { onWrite(message); return Task.CompletedTask; }
        public Task WriteAsync(WatchFileResponse message, CancellationToken cancellationToken) { onWrite(message); return Task.CompletedTask; }
    }

    private sealed class NullStringWriter : IServerStreamWriter<StringRequest>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(StringRequest message) => Task.CompletedTask;
    }

    private sealed class TestCallContext : ServerCallContext
    {
        private readonly CancellationToken token;
        private readonly Metadata headers;

        public TestCallContext(CancellationToken token = default, HttpContext? http = null, Metadata? headers = null)
        {
            this.token = token;
            this.headers = headers ?? new Metadata();
            if (http != null)
                UserState["__HttpContext"] = http;
        }

        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "127.0.0.1";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore => headers;
        protected override CancellationToken CancellationTokenCore => token;
        protected override Metadata ResponseTrailersCore => new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotImplementedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
