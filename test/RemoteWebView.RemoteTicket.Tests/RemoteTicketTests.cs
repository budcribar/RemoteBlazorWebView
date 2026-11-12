using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;

namespace RemoteWebView.RemoteTicket.Tests;

public class RemoteTicketTests
{
    private const string Secret = "relay-ticket-secret";
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

    private static RemoteTicketPayload Ticket(string oid, string[] groups, DateTimeOffset exp) => new()
    {
        Oid = oid,
        Name = "Pat Lee",
        Email = "pat@example.com",
        Groups = groups,
        Tier = "msp",
        Exp = exp.ToUnixTimeSeconds(),
    };

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };

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
