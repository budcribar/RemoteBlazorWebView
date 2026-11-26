using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

/// <summary>GetIds lists every session id on the relay; a relay that requires sign-in must not hand them to anyone.</summary>
public class SessionIdListingTests
{
    [Fact]
    public async Task GetIds_lists_sessions_when_the_relay_has_no_sign_in()
    {
        var id = Guid.NewGuid().ToString();
        var service = Create(new NoAuthorization(), id);

        var response = await service.GetIds(new Empty(), null!);

        Assert.Equal([id], response.Responses);
    }

    [Fact]
    public async Task GetIds_lists_sessions_when_no_authorization_is_registered()
    {
        var id = Guid.NewGuid().ToString();
        var service = Create(null, id);

        var response = await service.GetIds(new Empty(), null!);

        Assert.Equal([id], response.Responses);
    }

    [Fact]
    public async Task GetIds_is_refused_when_the_relay_requires_sign_in()
    {
        var service = Create(new FakeAuthorization(), Guid.NewGuid().ToString());

        var error = await Assert.ThrowsAsync<RpcException>(() => service.GetIds(new Empty(), null!));

        Assert.Equal(StatusCode.PermissionDenied, error.StatusCode);
    }

    private static RemoteWebViewService Create(IRelayAuthorization? authorization, string sessionId)
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        services[sessionId] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var files = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        return new RemoteWebViewService(
            NullLogger<RemoteWebViewService>.Instance,
            services,
            channels,
            new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services),
            files,
            authorization: authorization);
    }
}
