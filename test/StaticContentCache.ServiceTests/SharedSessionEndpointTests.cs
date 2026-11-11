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

    private static (DefaultHttpContext Context, ServiceState State, Guid Id) NewRequest(string query, ServiceState? existing = null, Guid? existingId = null, VoiceRoom? voice = null)
    {
        var id = existingId ?? Guid.NewGuid();
        var state = existing ?? new ServiceState(NullLogger<RemoteWebViewService>.Instance, true) { Id = id.ToString(), Voice = voice };
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
