using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.EndPoints;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

public class RefreshRaceTests
{
    [Fact]
    public async Task Stray_send_after_deregister_does_not_recreate_the_entry()
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var id = Guid.NewGuid().ToString();
        services[id] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(services.TryRemove(id, out _));

        var ipc = NewBrowserIpc(services);
        var started = Stopwatch.StartNew();
        var response = await ipc.SendMessage(new SendSequenceMessageRequest
        {
            Id = id,
            Sequence = 5,
            IsPrimary = true,
            Message = "late"
        }, null!);
        started.Stop();

        Assert.False(response.Success);
        Assert.False(services.ContainsKey(id));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Wait_returns_when_the_stored_object_differs()
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var id = Guid.NewGuid().ToString();
        services[id] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);

        var started = Stopwatch.StartNew();
        var waiting = Endpoints.WaitForClientShutdown(services, id, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));
        while (!waiting.IsCompleted)
        {
            services[id] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            await Task.Delay(10);
        }

        var gone = await waiting;
        started.Stop();

        Assert.True(gone);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Wait_times_out_when_the_same_object_stays()
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var id = Guid.NewGuid().ToString();
        services[id] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);

        var started = Stopwatch.StartNew();
        var gone = await Endpoints.WaitForClientShutdown(services, id, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(100));
        started.Stop();

        Assert.False(gone);
        Assert.True(services.ContainsKey(id));
        Assert.True(started.Elapsed >= TimeSpan.FromSeconds(30));
    }

    private static BrowserIPCService NewBrowserIpc(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> services)
    {
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services);
        return new BrowserIPCService(NullLogger<RemoteWebViewService>.Instance, services, shutdown);
    }
}
