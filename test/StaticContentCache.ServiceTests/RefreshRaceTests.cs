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
        var timeout = TimeSpan.FromMilliseconds(250);

        var started = Stopwatch.StartNew();
        var gone = await Endpoints.WaitForClientShutdown(services, id, timeout, TimeSpan.FromMilliseconds(50));
        started.Stop();

        Assert.False(gone);
        Assert.True(services.ContainsKey(id));
        Assert.True(started.Elapsed >= timeout);
    }

    // /status says "connected" only once an app has registered the id. The page endpoints add an unfinished entry for an id
    // a browser asks for before its app is up; the agent polls /status to learn when the app it started is ready, and that
    // entry must not tell it so.
    [Fact]
    public void Status_counts_only_a_finished_registration()
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var registered = Guid.NewGuid().ToString();
        var waitedFor = Guid.NewGuid().ToString();
        var failed = Guid.NewGuid().ToString();
        var done = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        done.SetResult(new ServiceState(NullLogger<RemoteWebViewService>.Instance, false) { Id = registered });
        services[registered] = done;
        services[waitedFor] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        cancelled.SetCanceled();
        services[failed] = cancelled;

        Assert.True(Endpoints.IsRegistered(services, registered));
        Assert.False(Endpoints.IsRegistered(services, waitedFor));
        Assert.False(Endpoints.IsRegistered(services, failed));
        Assert.False(Endpoints.IsRegistered(services, Guid.NewGuid().ToString()));
    }

    private static BrowserIPCService NewBrowserIpc(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> services)
    {
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services);
        return new BrowserIPCService(NullLogger<RemoteWebViewService>.Instance, services, shutdown);
    }
}
