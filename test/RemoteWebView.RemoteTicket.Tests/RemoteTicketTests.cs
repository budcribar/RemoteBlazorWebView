using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;

namespace RemoteWebView.RemoteTicket.Tests;

public class RemoteTicketTests
{
    private const string Secret = "relay-ticket-secret-for-remote-ticket-tests";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    [Fact]
    public void A_signed_ticket_reads_back_and_a_bad_signature_does_not()
    {
        var token = RemoteTicketFormat.Create(Secret, Ticket("user-1", ["bench"], Now.AddMinutes(5)));
        Assert.True(RemoteTicketFormat.TryRead(token, Secret, Now, out var payload, out _));
        Assert.Equal("user-1", payload!.Oid);
        Assert.Equal("Pat Lee", payload.Name);
        Assert.Equal("pat@example.com", payload.Email);
        Assert.Equal(["bench"], payload.Groups);
        Assert.Equal("msp", payload.Tier);
        Assert.True(RemoteTicketFormat.AllowsGroup(payload, "bench"));
        Assert.False(RemoteTicketFormat.AllowsGroup(payload, "other"));

        var flipped = token[..^1] + (token[^1] == 'A' ? "B" : "A");
        Assert.False(RemoteTicketFormat.TryRead(flipped, Secret, Now, out _, out var error));
        Assert.Equal("invalid_signature", error);
        Assert.False(RemoteTicketFormat.TryRead(token, "other-secret", Now, out _, out error));
        Assert.Equal("invalid_signature", error);
    }

    [Fact]
    public void An_expired_ticket_is_rejected()
    {
        var token = RemoteTicketFormat.Create(Secret, Ticket("user-1", ["bench"], Now.AddSeconds(-1)));
        Assert.False(RemoteTicketFormat.TryRead(token, Secret, Now, out _, out var error));
        Assert.Equal("expired", error);
    }

    [Fact]
    public async Task Group_lookups_are_cached_for_thirty_seconds_and_the_cap_is_enforced()
    {
        var clock = new ManualTime(Now);
        var handler = new AuthorityStub(_ => Json(new { group = "bench", active = true, planTier = "solo", maxConcurrentSessions = 1 }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://authority.example/") };
        var policy = new RemoteTicketSessionPolicy(http, Secret, clock, RemoteTicketSessionPolicy.CacheDuration, allowUnregisteredGroups: false);

        Assert.True((await policy.CheckCreateSessionAsync("bench", 0)).Allowed);
        Assert.True((await policy.CheckCreateSessionAsync("bench", 0)).Allowed);
        Assert.Equal(1, handler.GroupCalls);

        var capped = await policy.CheckCreateSessionAsync("bench", 1);
        Assert.False(capped.Allowed);
        Assert.Equal("session_limit_reached", capped.ErrorCode);
        Assert.Equal(1, handler.GroupCalls);

        clock.Utc = Now.AddSeconds(31);
        Assert.True((await policy.CheckCreateSessionAsync("bench", 0)).Allowed);
        Assert.Equal(2, handler.GroupCalls);
    }

    [Fact]
    public async Task An_inactive_group_is_denied_unless_unregistered_groups_are_allowed()
    {
        var handler = new AuthorityStub(_ => Json(new { group = "bench", active = false, planTier = "solo", maxConcurrentSessions = 1 }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://authority.example/") };
        var denied = new RemoteTicketSessionPolicy(http, Secret, new ManualTime(Now), false);
        var decision = await denied.CheckCreateSessionAsync("bench", 0);
        Assert.False(decision.Allowed);
        Assert.Equal("group_inactive", decision.ErrorCode);

        var allowed = new RemoteTicketSessionPolicy(http, Secret, new ManualTime(Now), true);
        Assert.True((await allowed.CheckCreateSessionAsync("bench", 0)).Allowed);
    }

    [Fact]
    public void Authorize_accepts_and_denies_and_fails_closed_when_the_authority_is_down()
    {
        var handler = new AuthorityStub(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var accepted = body.Contains("good-key", StringComparison.Ordinal);
            return Json(new { accepted, group = accepted ? "bench" : "", denialReason = accepted ? null : "Invalid shop enrollment key." });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://authority.example/") };
        var policy = new RemoteTicketAgentPolicy(http, Secret);

        var ok = policy.CheckEnrollment("good-key", new PeakSWC.RemoteWebView.Agents.AgentOptions());
        Assert.True(ok.Accepted);
        Assert.Equal("bench", ok.Group);
        Assert.Equal(Secret, handler.LastSecret);

        var denied = policy.CheckEnrollment("bad-key", new PeakSWC.RemoteWebView.Agents.AgentOptions());
        Assert.False(denied.Accepted);
        Assert.Equal("Invalid shop enrollment key.", denied.DenialReason);

        handler.Throw = true;
        var closed = policy.CheckEnrollment("good-key", new PeakSWC.RemoteWebView.Agents.AgentOptions());
        Assert.False(closed.Accepted);
        Assert.Equal("The relay authority could not be reached.", closed.DenialReason);
    }

    [Fact]
    public async Task Mirror_redirects_without_a_ticket_and_forbids_a_ticket_for_another_group()
    {
        var handler = new AuthorityStub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var host = await Start(handler);
        var state = AddSession(host, "bench");
        using var anon = Client(host);

        var missing = await anon.GetAsync($"mirror/{state.Id}");
        Assert.Equal(HttpStatusCode.Redirect, missing.StatusCode);
        var location = missing.Headers.Location!.ToString();
        Assert.StartsWith("https://admin.remoteblazorwebview.com/login?returnUrl=", location);
        Assert.Contains(Uri.EscapeDataString($"/mirror/{state.Id}"), location);

        var token = RemoteTicketFormat.Create(Secret, Ticket("user-9", ["other"], DateTimeOffset.UtcNow.AddMinutes(5)));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"mirror/{state.Id}");
        request.Headers.Add("Cookie", RemoteTicketReader.CookieName + "=" + token);
        var wrongGroup = await anon.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, wrongGroup.StatusCode);

        var allowedToken = RemoteTicketFormat.Create(Secret, Ticket("user-9", ["bench"], DateTimeOffset.UtcNow.AddMinutes(5)));
        using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, $"mirror/{state.Id}");
        allowedRequest.Headers.Add(RemoteTicketReader.HeaderName, allowedToken);
        var allowed = await anon.SendAsync(allowedRequest);
        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.Redirect, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, allowed.StatusCode);
    }

    [Theory]
    [InlineData("ticket")]
    [InlineData(RemoteTicketReader.CookieName)]
    public async Task Mirror_ignores_ticket_in_query_parameter(string paramName)
    {
        var handler = new AuthorityStub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        await using var host = await Start(handler);
        var state = AddSession(host, "bench");
        using var anon = Client(host);

        var token = RemoteTicketFormat.Create(Secret, Ticket("user-9", ["bench"], DateTimeOffset.UtcNow.AddMinutes(5)));
        var response = await anon.GetAsync($"mirror/{state.Id}?{paramName}={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out _));
    }

    [Fact]
    public void RemoteTicketFormat_rejects_legacy_raw_json_signatures_and_reads_role()
    {
        var now = DateTimeOffset.UtcNow;
        var payload = Ticket("user-canonical", ["bench"], now.AddHours(1), role: "tech");

        // Canonical format (signs base64url payload)
        var standardToken = RemoteTicketFormat.Create(Secret, payload);
        Assert.True(RemoteTicketFormat.TryRead(standardToken, Secret, now, out var standardParsed, out var standardErr), standardErr);
        Assert.NotNull(standardParsed);
        Assert.Equal("user-canonical", standardParsed.Oid);
        Assert.Equal("tech", standardParsed.Role);

        // Legacy raw-JSON signature format is rejected
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var b64Payload = Convert.ToBase64String(jsonBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var rawSigBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), jsonBytes);
        var b64Sig = Convert.ToBase64String(rawSigBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var legacyToken = b64Payload + "." + b64Sig;

        Assert.False(RemoteTicketFormat.TryRead(legacyToken, Secret, now, out _, out var legacyErr));
        Assert.Equal("invalid_signature", legacyErr);
    }

    [Fact]
    public void The_provider_needs_a_configured_signing_key_of_at_least_32_bytes()
    {
        static IConfiguration Config(string? key) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RelayTicket:SigningKey"] = key,
                    ["RelayTicket:AuthorityUrl"] = "https://authority.example",
                })
                .Build();

        Assert.Equal("", RemoteTicketSettings.From(Config(null)).SigningKey);
        Assert.Throws<InvalidOperationException>(() => new RemoteTicketAuthorization().ConfigureServices(new ServiceCollection(), Config(null)));
        Assert.Throws<InvalidOperationException>(() => new RemoteTicketAuthorization().ConfigureServices(new ServiceCollection(), Config("short-key")));
        new RemoteTicketAuthorization().ConfigureServices(new ServiceCollection(), Config(Secret));
    }

    [Fact]
    public void CheckAdminAccess_respects_authority_unattended_flag_and_role()
    {
        var handler = new AuthorityStub(request =>
        {
            var body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("viewer-ticket", StringComparison.Ordinal))
                return Json(new { accepted = true, group = "bench", role = "viewer", unattendedAuthorized = false });
            return Json(new { accepted = true, group = "bench", role = "tech", unattendedAuthorized = true });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://authority.example/") };
        var policy = new RemoteTicketAgentPolicy(http, Secret);

        var viewerCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        viewerCtx.Request.Headers[RemoteTicketReader.HeaderName] = "viewer-ticket";
        var viewerDecision = policy.CheckAdminAccess(viewerCtx, null, new PeakSWC.RemoteWebView.Agents.AgentOptions());
        Assert.True(viewerDecision.Allowed);
        Assert.False(viewerDecision.UnattendedAuthorized);
        Assert.Equal("viewer", viewerDecision.CallerRole);

        var techCtx = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        techCtx.Request.Headers[RemoteTicketReader.HeaderName] = "tech-ticket";
        var techDecision = policy.CheckAdminAccess(techCtx, null, new PeakSWC.RemoteWebView.Agents.AgentOptions());
        Assert.True(techDecision.Allowed);
        Assert.True(techDecision.UnattendedAuthorized);
        Assert.Equal("tech", techDecision.CallerRole);
    }

    [Fact]
    public void CheckEnrollment_forwards_machine_id_and_agent_token_and_returns_issued_token()
    {
        string? capturedBody = null;
        var handler = new AuthorityStub(request =>
        {
            capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return Json(new { accepted = true, group = "bench", issuedAgentToken = "rpd_agt_issued_123" });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://authority.example/") };
        var policy = new RemoteTicketAgentPolicy(http, Secret);

        var decision = policy.CheckEnrollment("good-key", "pc-01", "prev-token", new PeakSWC.RemoteWebView.Agents.AgentOptions());
        Assert.True(decision.Accepted);
        Assert.Equal("bench", decision.Group);
        Assert.Equal("rpd_agt_issued_123", decision.IssuedAgentToken);
        Assert.Contains("\"agentId\":\"pc-01\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"agentToken\":\"prev-token\"", capturedBody, StringComparison.Ordinal);
    }

    private static RemoteTicketPayload Ticket(string oid, string[] groups, DateTimeOffset exp, string role = "owner") => new()
    {
        Oid = oid,
        Name = "Pat Lee",
        Email = "pat@example.com",
        Groups = groups,
        Tier = "msp",
        Role = role,
        Exp = exp.ToUnixTimeSeconds(),
    };

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task The_relay_does_not_list_session_ids_when_tickets_are_required()
    {
        await using var server = await Start(new AuthorityStub(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        AddSession(server, "bench");

        // Built the way the gRPC host activates its services, so this checks the relay's own wiring.
        var service = ActivatorUtilities.CreateInstance<PeakSWC.RemoteWebView.RemoteWebViewService>(server.Services);
        var error = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => service.GetIds(new Google.Protobuf.WellKnownTypes.Empty(), null!));

        Assert.Equal(Grpc.Core.StatusCode.PermissionDenied, error.StatusCode);
    }

    [Fact]
    public async Task Client_listing_uses_the_signed_in_user_not_the_user_id_in_the_request()
    {
        await using var server = await Start(new AuthorityStub(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var mine = AddSession(server, "bench");
        AddSession(server, "other");

        // Another shop's user signs in first, so the relay knows their groups.
        await SignedIn(server, Ticket("user-other", ["other"], DateTimeOffset.UtcNow.AddMinutes(5)));

        var service = ActivatorUtilities.CreateInstance<ClientIPCService>(server.Services);
        var caller = await SignedIn(server, Ticket("user-bench", ["bench"], DateTimeOffset.UtcNow.AddMinutes(5)));

        var groups = await service.GetUserGroups(new UserRequest { Oid = "user-other" }, caller);
        Assert.Equal(["bench"], groups.Groups);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listed = await FirstClientList(service, new UserMessageRequest { Id = Guid.NewGuid().ToString(), Oid = "user-other" }, await SignedIn(server, Ticket("user-bench", ["bench"], DateTimeOffset.UtcNow.AddMinutes(5)), cts.Token));
        var only = Assert.Single(listed.ClientResponses);
        Assert.Equal(mine.Id, only.Id);
    }

    [Fact]
    public async Task Client_listing_refuses_a_call_without_a_valid_ticket()
    {
        await using var server = await Start(new AuthorityStub(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        AddSession(server, "bench");
        await SignedIn(server, Ticket("user-bench", ["bench"], DateTimeOffset.UtcNow.AddMinutes(5)));
        var service = ActivatorUtilities.CreateInstance<ClientIPCService>(server.Services);

        var anonymous = await SignedIn(server, null);
        var error = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => service.GetUserGroups(new UserRequest { Oid = "user-bench" }, anonymous));
        Assert.Equal(Grpc.Core.StatusCode.Unauthenticated, error.StatusCode);

        var expired = await SignedIn(server, Ticket("user-bench", ["bench"], DateTimeOffset.UtcNow.AddMinutes(-1)));
        error = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => service.GetUserGroups(new UserRequest { Oid = "user-bench" }, expired));
        Assert.Equal(Grpc.Core.StatusCode.Unauthenticated, error.StatusCode);

        var writer = new ListWriter(null);
        error = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => service.GetClients(new UserMessageRequest { Id = "x", Oid = "user-bench" }, writer, anonymous));
        Assert.Equal(Grpc.Core.StatusCode.Unauthenticated, error.StatusCode);
        Assert.Empty(writer.Written);
    }

    /// <summary>A gRPC call context for a request that carried <paramref name="ticket"/> (or none), authenticated by the relay's own scheme.</summary>
    private static async Task<CallContext> SignedIn(EmbeddedRemoteWebViewServer server, RemoteTicketPayload? ticket, CancellationToken cancellationToken = default)
    {
        // Each request gets its own scope, as in the relay; the scope lives as long as the test's server.
        var http = new DefaultHttpContext { RequestServices = server.Services.CreateScope().ServiceProvider };
        if (ticket != null)
            http.Request.Headers[RemoteTicketReader.HeaderName] = RemoteTicketFormat.Create(Secret, ticket);
        var result = await http.AuthenticateAsync(RemoteTicketAuthorization.Scheme);
        if (result.Succeeded)
            http.User = result.Principal!;
        return new CallContext(http, cancellationToken);
    }

    private static async Task<ClientResponseList> FirstClientList(ClientIPCService service, UserMessageRequest request, CallContext context)
    {
        var writer = new ListWriter(context.Cancel);
        try { await service.GetClients(request, writer, context); }
        catch (OperationCanceledException) { }
        return Assert.Single(writer.Written);
    }

    private sealed class ListWriter(Action? afterFirst) : Grpc.Core.IServerStreamWriter<ClientResponseList>
    {
        public List<ClientResponseList> Written { get; } = [];
        public Grpc.Core.WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(ClientResponseList message)
        {
            Written.Add(message);
            afterFirst?.Invoke();
            return Task.CompletedTask;
        }
    }

    private sealed class CallContext : Grpc.Core.ServerCallContext
    {
        private readonly CancellationTokenSource cts;

        public CallContext(HttpContext http, CancellationToken cancellationToken)
        {
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            UserState["__HttpContext"] = http; // where Grpc.AspNetCore's GetHttpContext() finds it
        }

        public void Cancel() => cts.Cancel();

        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Grpc.Core.Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => cts.Token;
        protected override Grpc.Core.Metadata ResponseTrailersCore { get; } = new();
        protected override Grpc.Core.Status StatusCore { get; set; }
        protected override Grpc.Core.WriteOptions? WriteOptionsCore { get; set; }
        protected override Grpc.Core.AuthContext AuthContextCore => null!;
        protected override Grpc.Core.ContextPropagationToken CreatePropagationTokenCore(Grpc.Core.ContextPropagationOptions? options) => null!;
        protected override Task WriteResponseHeadersAsyncCore(Grpc.Core.Metadata responseHeaders) => Task.CompletedTask;
    }

    private static async Task<EmbeddedRemoteWebViewServer> Start(AuthorityStub handler)
    {
        var options = new RemoteWebViewServerOptions { Authorization = new RemoteTicketAuthorization() };
        options.Configuration["environment"] = "Development";
        options.Configuration["RelayTicket:SigningKey"] = Secret;
        options.Configuration["RelayTicket:AuthorityUrl"] = "https://authority.example";
        options.Configuration["RelayTicket:LoginUrl"] = "https://admin.remoteblazorwebview.com/login";
        options.ConfigureTestServices = services => services.AddSingleton<RemoteTicketAuthorityHandler>(handler);
        return await RemoteWebViewServer.StartAsync(options);
    }

    private static ServiceState AddSession(EmbeddedRemoteWebViewServer server, string group)
    {
        var id = Guid.NewGuid().ToString();
        var state = new ServiceState(NullLogger<PeakSWC.RemoteWebView.RemoteWebViewService>.Instance, false)
        {
            Id = id,
            Group = group,
        };
        state.FileManagerReady.TrySetResult(true);
        var source = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(state);
        server.Services.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>()[id] = source;
        return state;
    }

    private static HttpClient Client(EmbeddedRemoteWebViewServer server) =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = server.BaseUri };

    private sealed class ManualTime(DateTimeOffset utc) : TimeProvider
    {
        public DateTimeOffset Utc { get; set; } = utc;
        public override DateTimeOffset GetUtcNow() => Utc;
    }

    private sealed class AuthorityStub(Func<HttpRequestMessage, HttpResponseMessage> respond) : RemoteTicketAuthorityHandler
    {
        public int GroupCalls { get; private set; }
        public string? LastSecret { get; private set; }
        public bool Throw { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Throw)
                throw new HttpRequestException("down");
            if (request.Headers.TryGetValues(RemoteTicketAuthority.SecretHeader, out var values))
                LastSecret = values.FirstOrDefault();
            if (request.RequestUri?.AbsolutePath.Contains("/api/relay/groups/", StringComparison.Ordinal) == true)
                GroupCalls++;
            return Task.FromResult(respond(request));
        }
    }
}
