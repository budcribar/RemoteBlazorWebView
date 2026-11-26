using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.EndPoints;

namespace StaticContentCache.Tests;

/// <summary>
/// The /app/{id} page in a shared session: the owner's own surface opens it with ?role=owner, and the remote user then joins
/// at /mirror/{id}. The redirect must keep the query (the page's script reads it) and the voice call's remote end must go to
/// the remote user's page, not the owner's.
/// </summary>
public class SharedSessionEndpointTests
{
    [Theory]
    [InlineData("?role=owner")]
    [InlineData("?role=owner&control=remote")]
    public async Task The_owners_app_page_redirect_keeps_the_query(string query)
    {
        var (context, state, id) = NewRequest(query);
        await using var _ = state;

        await Endpoints.Start()(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal($"/{id}{query}", context.Response.Headers.Location.ToString());
        Assert.True(state.OwnerHoldsApp);
        Assert.True(state.InUse);
    }

    [Theory]
    [InlineData("?role=owner&key=wrong")]
    [InlineData("?role=owner")]
    public async Task When_the_app_sent_an_owner_key_the_owner_page_needs_it(string query)
    {
        var (context, state, _) = NewRequest(query, ownerKey: "app-secret");

        await Endpoints.Start()(context);

        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.False(state.OwnerHoldsApp);
        Assert.False(state.InUse); // the app's own window can still open it
        Assert.Empty(context.Response.Headers.SetCookie.ToString());
    }

    [Theory]
    [InlineData("?role=owner&key=app-secret", "?role=owner")]
    [InlineData("?role=owner&control=remote&key=app-secret", "?role=owner&control=remote")]
    public async Task The_owner_page_with_the_key_gets_the_owner_cookie_and_the_key_leaves_the_address(string query, string kept)
    {
        var (context, state, id) = NewRequest(query, ownerKey: "app-secret");

        await Endpoints.Start()(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal($"/{id}{kept}", context.Response.Headers.Location.ToString());
        Assert.True(state.OwnerHoldsApp);
        Assert.Contains($"{OwnerAccess.CookieName(id.ToString())}={state.OwnerToken}", context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task A_plain_app_page_redirects_without_a_query_and_is_not_the_owner()
    {
        var (context, state, id) = NewRequest("");
        await using var _ = state;

        await Endpoints.Start()(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal($"/{id}", context.Response.Headers.Location.ToString());
        Assert.False(state.OwnerHoldsApp);
        Assert.True(state.InUse);
    }

    [Fact]
    public async Task A_second_visitor_to_the_app_page_is_locked_out()
    {
        var (first, state, id) = NewRequest("?role=owner");
        await using var _ = state;
        await Endpoints.Start()(first);

        var (second, _, _) = NewRequest("", state, id);
        await Endpoints.Start()(second);

        Assert.Equal(StatusCodes.Status409Conflict, second.Response.StatusCode);
    }

    // After the PC restarts, a browser tab left on the session can reach the new copy of the app before the app's own
    // window does. In a shared session it is sent to the mirror instead of taking the app page, so the window, proven by
    // the app's key, gets its page and nothing the other browser does can count as a refresh (RemoteWebViewAdmin#24).
    [Fact]
    public async Task The_owner_with_the_key_gets_the_app_page_after_a_browser_that_came_back_first()
    {
        var (remote, state, id) = NewRequest("", ownerKey: "app-secret");
        await using var _ = state;
        await Endpoints.Start()(remote);
        Assert.Equal($"/mirror/{id}", remote.Response.Headers.Location.ToString());
        Assert.False(state.InUse);

        var (owner, _, _) = NewRequest("?role=owner&key=app-secret", state, id);
        await Endpoints.Start()(owner);

        Assert.Equal(StatusCodes.Status302Found, owner.Response.StatusCode);
        Assert.Equal($"/{id}?role=owner", owner.Response.Headers.Location.ToString());
        Assert.True(state.OwnerHoldsApp);
        Assert.True(state.InUse);
        Assert.Contains($"{OwnerAccess.CookieName(id.ToString())}={state.OwnerToken}", owner.Response.Headers.SetCookie.ToString());
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("?control=remote", "?control=remote")]
    [InlineData("?key=app-secret", "")] // a key without role=owner is not the owner page, and the key never travels on
    public async Task In_a_shared_session_a_browser_without_the_owner_key_is_sent_from_the_app_page_to_the_mirror(string query, string kept)
    {
        using var lifetime = new CancellationTokenSource();
        var room = VoiceRoom.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), lifetime.Token)!;
        var (context, state, id) = NewRequest(query, voice: room, ownerKey: "app-secret");
        await using var _ = state;

        await Endpoints.Start()(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal($"/mirror/{id}{kept}", context.Response.Headers.Location.ToString());
        Assert.False(state.InUse);           // the app page is still free for the app's own window
        Assert.Equal("", state.User);
        Assert.NotNull(room.TakeControllerForMirror()); // the remote end of the call is still there for the mirror page
    }

    [Fact]
    public async Task In_a_shared_session_a_browser_is_sent_to_the_mirror_even_while_the_owner_holds_the_app_page()
    {
        var (owner, state, id) = NewRequest("?role=owner&key=app-secret", ownerKey: "app-secret");
        await using var _ = state;
        await Endpoints.Start()(owner);

        var (remote, _, _) = NewRequest("", state, id);
        await Endpoints.Start()(remote);

        Assert.Equal(StatusCodes.Status302Found, remote.Response.StatusCode); // not the 409 locked page, whose button restarts the app
        Assert.Equal($"/mirror/{id}", remote.Response.Headers.Location.ToString());
    }

    [Fact]
    public async Task A_keyed_session_without_mirrors_keeps_the_app_page_for_whoever_comes_first()
    {
        var (context, state, id) = NewRequest("", ownerKey: "app-secret");
        await using var _ = state;
        state.EnableMirrors = false;

        await Endpoints.Start()(context);

        Assert.Equal($"/{id}", context.Response.Headers.Location.ToString());
        Assert.True(state.InUse);
    }

    // The restart after a reboot: a remote tab reloads /{id} after the app's page was served. It must reach the mirror,
    // never the refresh that restarts the app.
    [Fact]
    public async Task In_a_shared_session_a_load_of_the_app_page_without_the_owner_cookie_goes_to_the_mirror_not_a_refresh()
    {
        var (context, state, id) = NewRequest("", ownerKey: "app-secret");
        await using var _ = state;
        state.FileManagerReady.SetResult(true);
        state.Refresh = true; // the app's page has been served

        await Endpoints.StartOrRefresh()(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal($"/mirror/{id}", context.Response.Headers.Location.ToString());
        Assert.False(state.Token.IsCancellationRequested); // the session goes on
    }

    [Fact]
    public async Task Only_a_shared_session_with_mirrors_sends_browsers_without_owner_proof_to_the_mirror()
    {
        var (plain, keyed, id) = NewRequest("", ownerKey: "app-secret");
        await using var _ = keyed;
        Assert.True(Endpoints.SendsToMirror(keyed, plain));

        var (withCookie, _, _) = NewRequest("", keyed, id);
        withCookie.Request.Headers.Cookie = $"{OwnerAccess.CookieName(id.ToString())}={keyed.OwnerToken}";
        Assert.False(Endpoints.SendsToMirror(keyed, withCookie));

        var (withKey, _, _) = NewRequest("?role=owner&key=app-secret", keyed, id);
        Assert.False(Endpoints.SendsToMirror(keyed, withKey));

        var (other, unkeyed, _) = NewRequest("");
        await using var __ = unkeyed;
        Assert.False(Endpoints.SendsToMirror(unkeyed, other)); // older apps and remote components: unchanged
    }

    // A browser that comes back after a reboot can get to the mirror before the app's own window has opened its page.
    // It is asked to try again (503, Retry-After, a page that reloads itself), not left on a bare error.
    [Fact]
    public async Task A_mirror_before_the_apps_page_is_open_waits_and_retries()
    {
        var (context, state, _) = NewRequest("", ownerKey: "app-secret");
        await using var _ = state;
        state.FileManagerReady.SetResult(true);
        using var gone = new CancellationTokenSource();
        gone.Cancel(); // skip the endpoint's own short wait for the app's page
        context.RequestAborted = gone.Token;

        await Endpoints.Mirror()(context);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        Assert.Equal("3", context.Response.Headers.RetryAfter.ToString());
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains("http-equiv='refresh'", body);
        Assert.Empty(state.IsMirroredConnection);
    }

    [Fact]
    public async Task Without_an_owner_key_a_second_visitor_claiming_owner_is_still_locked_out()
    {
        var (first, state, id) = NewRequest("");
        await using var _ = state;
        await Endpoints.Start()(first);

        var (second, _, _) = NewRequest("?role=owner", state, id);
        await Endpoints.Start()(second);

        Assert.Equal(StatusCodes.Status409Conflict, second.Response.StatusCode);
        Assert.False(state.OwnerHoldsApp);
    }

    [Fact]
    public async Task The_first_load_of_the_page_is_served_and_a_later_one_is_a_refresh()
    {
        var (context, state, _) = NewRequest("", ownerKey: "app-secret");
        await using var _ = state;

        Assert.True(Endpoints.ServesPage(state, context));

        state.Refresh = true; // the page has been served once
        Assert.False(Endpoints.ServesPage(state, context));
    }

    // The failure this guards against: the owner's window loaded /{id} second, the relay took that for a refresh, and
    // the app restarted itself seconds after coming back from a reboot.
    [Fact]
    public async Task The_owners_first_load_is_served_even_after_another_browser_loaded_the_page()
    {
        var (owner, state, id) = NewRequest("?role=owner", ownerKey: "app-secret");
        await using var _ = state;
        owner.Request.Headers.Cookie = $"{OwnerAccess.CookieName(id.ToString())}={state.OwnerToken}";
        state.Refresh = true; // another browser was first

        Assert.True(Endpoints.ServesPage(state, owner));

        state.OwnerPageServed = true; // the owner has its page: loading it again is a refresh, as for anyone
        Assert.False(Endpoints.ServesPage(state, owner));
    }

    [Theory]
    [InlineData("wrong-token")]
    [InlineData("")]
    public async Task A_load_without_the_owner_token_after_the_first_is_a_refresh(string token)
    {
        var (context, state, id) = NewRequest("?role=owner", ownerKey: "app-secret");
        await using var _ = state;
        if (token.Length > 0)
            context.Request.Headers.Cookie = $"{OwnerAccess.CookieName(id.ToString())}={token}";
        state.Refresh = true;

        Assert.False(Endpoints.ServesPage(state, context));
    }

    [Fact]
    public async Task In_a_session_without_an_owner_key_the_owner_cookie_changes_nothing()
    {
        var (context, state, id) = NewRequest("?role=owner");
        await using var _ = state;
        context.Request.Headers.Cookie = $"{OwnerAccess.CookieName(id.ToString())}={state.OwnerToken}";
        state.Refresh = true;

        Assert.False(Endpoints.ServesPage(state, context));
    }

    [Fact]
    public async Task The_owners_app_page_does_not_take_the_remote_end_of_the_voice_call()
    {
        using var lifetime = new CancellationTokenSource();
        var room = VoiceRoom.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), lifetime.Token)!;
        var (context, state, _) = NewRequest("?role=owner", voice: room);
        await using var _ = state;

        await Endpoints.Start()(context);

        Assert.Empty(context.Response.Headers.SetCookie.ToString()); // no voice grant for the owner's page
        Assert.True(state.OwnerHoldsApp);
        Assert.NotNull(room.TakeControllerForMirror()); // still available for the remote user's mirror page
    }

    [Fact]
    public async Task A_plain_app_page_is_granted_the_remote_end_of_the_voice_call()
    {
        using var lifetime = new CancellationTokenSource();
        var room = VoiceRoom.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), lifetime.Token)!;
        var (context, state, _) = NewRequest("", voice: room);
        await using var _ = state;

        await Endpoints.Start()(context);

        Assert.NotEmpty(context.Response.Headers.SetCookie.ToString());
        Assert.Null(room.TakeControllerForMirror()); // already granted to the /app page: a mirror must not get a second one
    }

    [Fact]
    public void While_the_remote_user_is_on_the_call_no_other_mirror_page_gets_voice()
    {
        using var lifetime = new CancellationTokenSource();
        var room = VoiceRoom.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), lifetime.Token)!;

        var key = room.TakeControllerForMirror()!;
        Assert.NotNull(room.Join(key)); // the remote user's page connects

        Assert.Null(room.TakeControllerForMirror()); // a second mirror (a read-only viewer) gets no voice
        Assert.Null(room.Join(key));                 // and the key cannot connect a second remote end
    }

    [Fact]
    public void The_remote_user_gets_voice_again_after_refreshing()
    {
        using var lifetime = new CancellationTokenSource();
        var room = VoiceRoom.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), lifetime.Token)!;
        var first = room.Join(room.TakeControllerForMirror()!)!;

        room.Leave(first); // the page went away (refresh)

        var key = room.TakeControllerForMirror();
        Assert.False(string.IsNullOrEmpty(key));
        Assert.NotNull(room.Join(key!));
    }

    [Fact]
    public void A_mirror_page_that_never_connects_does_not_use_up_the_voice_grant()
    {
        using var lifetime = new CancellationTokenSource();
        var room = VoiceRoom.Create(Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), lifetime.Token)!;

        Assert.NotNull(room.TakeControllerForMirror()); // a chat app fetched the link for a preview and never connected

        var key = room.TakeControllerForMirror();       // the real remote user
        Assert.False(string.IsNullOrEmpty(key));
        Assert.NotNull(room.Join(key!));
    }

    // RemoteWebViewAdmin#22: the relay log says which browser asked for which page and what the relay did, without the key.
    [Fact]
    public async Task Each_page_request_is_logged_with_its_decision_and_never_the_owner_key()
    {
        var (context, state, id) = NewRequest("?key=app-secret", ownerKey: "app-secret");
        await using var _ = state;
        var lines = new List<string>();
        var services = new ServiceCollection();
        services.AddLogging(); // Information and up, the level production keeps for this category
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider>(new LineCollector(lines));
        services.AddSingleton(context.RequestServices.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>());
        services.AddSingleton(new ConcurrentDictionary<string, Channel<string>>());
        context.RequestServices = services.BuildServiceProvider();
        context.Request.Path = $"/app/{id}";
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.7");

        await Endpoints.Start()(context);

        var line = Assert.Single(lines);
        Assert.Contains($"Page /app/{id} for session {id}: sent to /mirror", line);
        Assert.Contains("203.0.113.7", line);
        Assert.Contains("owner proof: owner key", line); // a matching key, but not on the owner page (no role=owner)
        Assert.DoesNotContain("app-secret", line);
    }

    private sealed class LineCollector(List<string> lines) : Microsoft.Extensions.Logging.ILoggerProvider, Microsoft.Extensions.Logging.ILogger
    {
        public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => this;
        public void Dispose() { }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (lines) lines.Add(formatter(state, exception));
        }
    }

    private static (DefaultHttpContext Context, ServiceState State, Guid Id) NewRequest(string query, ServiceState? existing = null, Guid? existingId = null, VoiceRoom? voice = null, string ownerKey = "")
    {
        var id = existingId ?? Guid.NewGuid();
        var state = existing ?? new ServiceState(NullLogger<RemoteWebViewService>.Instance, true) { Id = id.ToString(), Voice = voice, OwnerKey = ownerKey };
        var clients = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var source = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.SetResult(state);
        clients[id.ToString()] = source;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clients);
        services.AddSingleton(new ConcurrentDictionary<string, Channel<string>>());

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.RouteValues["id"] = id.ToString();
        context.Request.QueryString = new QueryString(query);
        context.Response.Body = new MemoryStream();
        return (context, state, id);
    }
}
