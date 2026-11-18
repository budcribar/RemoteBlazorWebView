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
    // window does. The window, proven by the app's key, still gets in; the other browser's name stays on the session.
    [Fact]
    public async Task The_owner_with_the_key_is_not_locked_out_by_a_browser_that_took_the_app_page_first()
    {
        var (remote, state, id) = NewRequest("", ownerKey: "app-secret");
        await using var _ = state;
        await Endpoints.Start()(remote);
        Assert.True(state.InUse);
        state.User = "Remote Tech";

        var (owner, _, _) = NewRequest("?role=owner&key=app-secret", state, id);
        await Endpoints.Start()(owner);

        Assert.Equal(StatusCodes.Status302Found, owner.Response.StatusCode);
        Assert.Equal($"/{id}?role=owner", owner.Response.Headers.Location.ToString());
        Assert.True(state.OwnerHoldsApp);
        Assert.True(state.InUse);
        Assert.Equal("Remote Tech", state.User);
        Assert.Contains($"{OwnerAccess.CookieName(id.ToString())}={state.OwnerToken}", owner.Response.Headers.SetCookie.ToString());
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
