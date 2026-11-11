extern alias client;

using System.Collections.Concurrent;
using System.Net;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using Client = client::PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

/// <summary>
/// Who may open a session's pages when the relay requires sign-in, on a real (embedded) relay with the fake sign-in:
/// the requests say who they are, everything after that is the code production runs.
/// </summary>
public class AuthorizationTests
{
    private const string OwnerKey = "app-secret";

    private static async Task<(EmbeddedRemoteWebViewServer Server, ServiceState State)> StartAsync(IRelayAuthorization? authorization, string group = "bench")
    {
        var server = await RemoteWebViewServer.StartAsync(new RemoteWebViewServerOptions { Authorization = authorization });
        var id = Guid.NewGuid().ToString();
        var state = new ServiceState(NullLogger<PeakSWC.RemoteWebView.RemoteWebViewService>.Instance, true) { Id = id, Markup = "", Group = group, OwnerKey = OwnerKey };
        var source = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(state);
        server.Services.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>()[id] = source;
        return (server, state);
    }

    private static HttpClient Http(EmbeddedRemoteWebViewServer server) =>
        new(new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false }) { BaseAddress = server.BaseUri };

    private static async Task<HttpResponseMessage> Get(HttpClient http, string path, string? user = null, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (user != null) request.Headers.Add(FakeAuthorization.Header, user);
        if (cookie != null) request.Headers.Add("Cookie", cookie);
        return await http.SendAsync(request);
    }

    [Fact]
    public async Task Without_a_provider_session_pages_are_open()
    {
        var (server, state) = await StartAsync(null);
        await using var _ = server;
        using var http = Http(server);

        Assert.Equal(HttpStatusCode.OK, (await Get(http, $"status/{state.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_session_page_needs_a_signed_in_user()
    {
        var (server, state) = await StartAsync(new FakeAuthorization());
        await using var _ = server;
        using var http = Http(server);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(http, $"status/{state.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(http, $"mirror/{state.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(http, "health")).StatusCode); // not a session page
    }

    [Fact]
    public async Task A_signed_in_user_in_the_sessions_group_gets_in()
    {
        var (server, state) = await StartAsync(new FakeAuthorization());
        await using var _ = server;
        using var http = Http(server);

        var response = await Get(http, $"status/{state.Id}", FakeAuthorization.User("Pat Lee", "other", "bench"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_signed_in_user_outside_the_sessions_group_is_refused()
    {
        var (server, state) = await StartAsync(new FakeAuthorization());
        await using var _ = server;
        using var http = Http(server);

        // A forwarded link: signed in, but the session is not in any of this user's groups.
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(http, $"status/{state.Id}", FakeAuthorization.User("Sam", "other"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(http, $"mirror/{state.Id}", FakeAuthorization.User("Sam", "other"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(http, $"status/{state.Id}", FakeAuthorization.User("Nobody"))).StatusCode);
    }

    [Fact]
    public async Task The_fake_user_in_the_address_signs_a_browser_in_with_a_cookie()
    {
        var (server, state) = await StartAsync(new FakeAuthorization());
        await using var _ = server;
        using var http = Http(server);

        var first = await Get(http, $"status/{state.Id}?{FakeAuthorization.Query}={Uri.EscapeDataString(FakeAuthorization.User("Pat", "bench"))}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var cookie = Assert.Single(first.Headers.GetValues("Set-Cookie")).Split(';')[0];
        Assert.StartsWith(FakeAuthorization.Cookie + "=", cookie);

        Assert.Equal(HttpStatusCode.OK, (await Get(http, $"status/{state.Id}", cookie: cookie)).StatusCode);
    }

    [Fact]
    public async Task The_apps_own_window_opens_the_owner_page_without_signing_in()
    {
        var (server, state) = await StartAsync(new FakeAuthorization());
        await using var _ = server;
        using var http = Http(server);

        var page = await Get(http, $"app/{state.Id}?role=owner&key={OwnerKey}");

        Assert.Equal(HttpStatusCode.Redirect, page.StatusCode);
        Assert.Equal($"/{state.Id}?role=owner", page.Headers.Location?.ToString());
        var cookie = page.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(OwnerAccess.CookieName(state.Id), StringComparison.Ordinal)).Split(';')[0];

        // From then on the owner cookie stands for the key on the session's other pages.
        Assert.Equal(HttpStatusCode.OK, (await Get(http, $"status/{state.Id}", cookie: cookie)).StatusCode);
    }

    [Theory]
    [InlineData("?role=owner&key=wrong")]
    [InlineData("?role=owner")]
    public async Task The_owner_page_without_the_apps_key_is_like_any_other_visitor(string query)
    {
        var (server, state) = await StartAsync(new FakeAuthorization());
        await using var _ = server;
        using var http = Http(server);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(http, $"app/{state.Id}{query}")).StatusCode);
        // Signed in and in the group, the page still does not become the owner's without the key.
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(http, $"app/{state.Id}{query}", FakeAuthorization.User("Pat", "bench"))).StatusCode);
        Assert.False(state.OwnerHoldsApp);
    }

    [Fact]
    public async Task One_sessions_owner_cookie_does_not_open_another_session()
    {
        var (server, state) = await StartAsync(new FakeAuthorization());
        await using var _ = server;
        using var http = Http(server);
        var other = new ServiceState(NullLogger<PeakSWC.RemoteWebView.RemoteWebViewService>.Instance, true) { Id = Guid.NewGuid().ToString(), Group = "bench", OwnerKey = "another-secret" };
        var source = new TaskCompletionSource<ServiceState>();
        source.SetResult(other);
        server.Services.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>()[other.Id] = source;

        var cookie = $"{OwnerAccess.CookieName(other.Id)}={state.OwnerToken}"; // the first session's token under the second's name

        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(http, $"status/{other.Id}", cookie: cookie)).StatusCode);
    }

    [Fact]
    public async Task The_roster_carries_the_signed_in_name()
    {
        var (server, state) = await StartAsync(new FakeAuthorization());
        await using var _ = server;
        using var channel = GrpcChannel.ForAddress(server.GrpcUri, new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() });
        var browser = new Client.BrowserIPC.BrowserIPCClient(channel);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using var call = browser.ReceiveMessage(new Client.ReceiveMessageRequest { Id = state.Id, ClientId = "remote" },
            new Metadata { { FakeAuthorization.Header, FakeAuthorization.User("Pat Lee", "bench") } }, cancellationToken: timeout.Token);

        string? roster = null;
        while (roster is null && await call.ResponseStream.MoveNext(timeout.Token))
            if (call.ResponseStream.Current.Request.StartsWith("participants:", StringComparison.Ordinal)) roster = call.ResponseStream.Current.Request;

        Assert.Contains("\"id\":\"remote\"", roster);
        Assert.Contains("\"name\":\"Pat Lee\"", roster);
    }

    private static IConfiguration Settings(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    [Fact]
    public void With_no_setting_there_is_no_sign_in() =>
        Assert.IsType<NoAuthorization>(RelayAuthorization.Resolve(Settings()));

    [Fact]
    public void The_fake_provider_is_refused_outside_development()
    {
        Assert.IsType<FakeAuthorization>(RelayAuthorization.Resolve(Settings((RelayAuthorization.ProviderKey, "Fake"), ("environment", "Development"))));

        Assert.Throws<InvalidOperationException>(() => RelayAuthorization.Resolve(Settings((RelayAuthorization.ProviderKey, "Fake"), ("environment", "Production"))));
        Assert.Throws<InvalidOperationException>(() => RelayAuthorization.Resolve(Settings((RelayAuthorization.ProviderKey, "fake"))));
    }

    [Fact]
    public void A_provider_this_build_does_not_have_stops_the_relay_instead_of_running_open()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => RelayAuthorization.Resolve(Settings((RelayAuthorization.ProviderKey, "NoSuchProvider"))));
        Assert.Contains("NoSuchProvider", ex.Message);
    }
}
