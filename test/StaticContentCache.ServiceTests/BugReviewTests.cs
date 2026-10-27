using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;

namespace StaticContentCache.Tests;

public class BugReviewTests
{
    [Fact]
    public async Task One_faulted_registration_does_not_hide_the_other_clients()
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var goodId = Guid.NewGuid().ToString();
        var good = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        good.SetResult(new ServiceState(NullLogger<RemoteWebViewService>.Instance, false)
        {
            Id = goodId,
            HostName = "host-a"
        });
        services[goodId] = good;

        var failed = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        failed.SetException(new InvalidOperationException("registration failed"));
        services[Guid.NewGuid().ToString()] = failed;

        var ipc = new ClientIPCService(
            NullLogger<ClientIPCService>.Instance,
            new ConcurrentDictionary<string, Channel<string>>(),
            services,
            null!,
            new RemoteFilesOptions());

        var response = await ipc.GetServerStatus(new Empty(), null!);

        Assert.Contains(response.ConnectionResponses, client => client.Id == goodId);
    }
}
