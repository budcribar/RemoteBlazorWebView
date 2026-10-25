extern alias client;

using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;
using Microsoft.Web.WebView2.Core;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using ClientRemote = client::PeakSWC.RemoteWebView;
using ServiceIpc = PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

public class ReconnectDisposeTests
{
    private const string AppOrigin = "https://0.0.0.1/";
    private const string NotifierSelector = "body::after";

    static ReconnectDisposeTests()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }

    [Fact]
    public async Task Hot_reload_attachment_adds_the_notifier_once_per_manager()
    {
        var type = typeof(StaticContentHotReloadManager).GetNestedType("AttachmentState", BindingFlags.NonPublic);
        Assert.NotNull(type);
        var adds = 0;
        var ctor = type!.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single();
        var state = ctor.Invoke(
        [
            (Func<Task>)(() =>
            {
                adds++;
                return Task.CompletedTask;
            }),
            (Func<Task>)(() => Task.CompletedTask)
        ]);
        var attach = type.GetMethod("Attach")!;
        var first = (Task)attach.Invoke(state, null)!;
        var second = (Task)attach.Invoke(state, null)!;
        await first;
        await second;

        Assert.Same(first, second);
        Assert.Equal(1, adds);
    }

    [Fact]
    public async Task Reattach_keeps_one_root_and_does_not_duplicate_the_hot_reload_notifier()
    {
        var stats = new PageStats();
        await using var manager = CreateManager(stats);
        await manager.AddRootComponentAsync(typeof(CountingRoot), "#app", ParameterView.Empty);

        var firstAttach = StaticContentHotReloadManager.TryAttachToWebViewManager(manager);
        var secondAttach = StaticContentHotReloadManager.TryAttachToWebViewManager(manager);
        if (MetadataUpdaterSupported)
        {
            Assert.NotNull(firstAttach);
            Assert.Same(firstAttach, secondAttach);
            await firstAttach!;
        }
        else
        {
            Assert.Null(firstAttach);
            Assert.Null(secondAttach);
        }

        await manager.AttachPageAsync();
        Assert.Equal(1, stats.RootsLive);
        Assert.Equal(1, stats.RootsCreated);
        Assert.Equal(MetadataUpdaterSupported ? 1 : 0, manager.CountSelector(NotifierSelector));
        AssertNoUnhandledDisconnect(manager);

        await manager.AttachPageAsync();

        Assert.Equal(1, stats.RootsLive);
        Assert.Equal(2, stats.RootsCreated);
        // One AttachToDocument per page load. A second registration would send two in the first page.
        Assert.Equal(MetadataUpdaterSupported ? 2 : 0, manager.CountSelector(NotifierSelector));
        AssertNoUnhandledDisconnect(manager);
    }

    [Fact]
    public async Task Reattach_component_that_calls_js_runtime_on_dispose_does_not_surface_an_unhandled_disconnect()
    {
        var stats = new PageStats { RethrowJs = false };
        await using var manager = CreateManager(stats);
        await manager.AddRootComponentAsync(typeof(JsDisposeComponent), "#app", ParameterView.Empty);
        await manager.AttachPageAsync();
        Assert.Equal(1, stats.JsLive);

        await manager.AttachPageAsync();

        Assert.Equal(1, stats.JsDisposeCalls);
        Assert.Equal(1, stats.JsLive);
        Assert.Equal(2, stats.JsCreated);
        var disconnect = Assert.IsType<JSDisconnectedException>(stats.JsDisposeException);
        Assert.Contains("JavaScript interop calls cannot be issued", disconnect.Message, StringComparison.Ordinal);
        AssertNoUnhandledDisconnect(manager);
    }

    [Fact]
    public async Task Dispose_shuts_down_the_grpc_channel_while_the_browser_is_connected_and_after_a_drop()
    {
        await using var host = await IpcHost.StartAsync();

        var connected = await ConnectedClient.StartAsync(host);
        await using var _ = connected;
        await connected.SignalConnectedAsync();
        Assert.False(connected.ChannelDisposed);
        await connected.Remote.DisposeAsyncCore();
        Assert.True(connected.ChannelDisposed);

        var dropped = await ConnectedClient.StartAsync(host);
        await using var __ = dropped;
        await dropped.SignalShutdownAsync();
        Assert.False(dropped.ChannelDisposed);
        await dropped.Remote.DisposeAsyncCore();
        Assert.True(dropped.ChannelDisposed);
    }

    [Fact]
    public async Task Js_disconnect_from_component_dispose_still_shuts_down_the_grpc_client()
    {
        await using var host = await IpcHost.StartAsync();
        var client = await ConnectedClient.StartAsync(host);
        await using var _ = client;
        var stats = new PageStats { RethrowJs = true };
        await using var manager = CreateManager(stats);
        await manager.AddRootComponentAsync(typeof(JsDisposeComponent), "#app", ParameterView.Empty);
        await manager.AttachPageAsync();

        Exception? escaped = null;
        try
        {
            // Same order as RemoteWebView2Manager.DisposeAsyncCore before the finally: page first.
            await manager.DisposeAsync();
        }
        catch (Exception ex)
        {
            escaped = ex;
        }

        Assert.False(client.ChannelDisposed);
        await client.Remote.DisposeAsyncCore();

        // The rethrown JSDisconnectedException is reported by WebViewManager (NotifyUnhandledException)
        // and does not fault DisposeAsync. RemoteWebView2Manager.DisposeAsyncCore can still dispose the
        // gRPC client after that base call returns.
        Assert.Null(escaped);
        Assert.Contains(manager.Unhandled, ex => ex is JSDisconnectedException);
        Assert.Equal(1, stats.JsDisposeCalls);
        Assert.IsType<JSDisconnectedException>(stats.JsDisposeException);
        Assert.True(client.ChannelDisposed);
    }

    [Fact]
    public void RemoteWebView2Manager_disposes_the_page_before_the_grpc_client()
    {
        var source = File.ReadAllText(Path.Combine(AllowExternalDropTests.RepoRoot(), "src", "SharedSource", "RemoteWebView2Manager.cs"));
        var page = source.IndexOf("await base.DisposeAsyncCore()", StringComparison.Ordinal);
        var client = source.IndexOf("await RemoteWebView.DisposeAsyncCore()", StringComparison.Ordinal);
        Assert.True(page >= 0 && client > page);
    }

    private static bool MetadataUpdaterSupported =>
        System.Reflection.Metadata.MetadataUpdater.IsSupported;

    private static void AssertNoUnhandledDisconnect(TestWebViewManager manager)
    {
        Assert.DoesNotContain(manager.Unhandled, ex => ex is JSDisconnectedException);
        Assert.DoesNotContain(
            manager.Messages,
            message => message.Contains("NotifyUnhandledException", StringComparison.Ordinal)
                && message.Contains("JavaScript interop calls cannot be issued", StringComparison.Ordinal));
    }

    private static TestWebViewManager CreateManager(PageStats stats)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(stats);
        services.AddBlazorWebView();
        return new TestWebViewManager(services.BuildServiceProvider());
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.Flatten().InnerExceptions)
            {
                foreach (var nested in Flatten(inner))
                    yield return nested;
            }

            yield break;
        }

        yield return exception;
        if (exception.InnerException is not null)
        {
            foreach (var inner in Flatten(exception.InnerException))
                yield return inner;
        }
    }

    private sealed class PageStats
    {
        public int RootsCreated;
        public int RootsLive;
        public int JsCreated;
        public int JsLive;
        public int JsDisposeCalls;
        public Exception? JsDisposeException;
        public bool RethrowJs;
    }

    private sealed class CountingRoot : ComponentBase, IDisposable
    {
        [Inject] public PageStats Stats { get; set; } = default!;

        protected override void OnInitialized()
        {
            Stats.RootsCreated++;
            Stats.RootsLive++;
        }

        public void Dispose() => Stats.RootsLive--;
    }

    private sealed class JsDisposeComponent : ComponentBase, IDisposable
    {
        [Inject] public PageStats Stats { get; set; } = default!;
        [Inject] public IJSRuntime Js { get; set; } = default!;

        protected override void OnInitialized()
        {
            Stats.JsCreated++;
            Stats.JsLive++;
        }

        public void Dispose()
        {
            Stats.JsLive--;
            Stats.JsDisposeCalls++;
            try
            {
                Js.InvokeVoidAsync("remoteblazor.disposeProbe").AsTask().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Stats.JsDisposeException = ex;
                if (Stats.RethrowJs)
                    throw;
            }
        }
    }

    private sealed class TestWebViewManager : WebViewManager
    {
        private readonly InlineDispatcher _dispatcher;
        public List<string> Messages { get; } = [];
        public List<Exception> Unhandled { get; } = [];

        public TestWebViewManager(IServiceProvider services)
            : this(new InlineDispatcher(), services)
        {
        }

        private TestWebViewManager(InlineDispatcher dispatcher, IServiceProvider services)
            : base(services, dispatcher, new Uri(AppOrigin), new NullFileProvider(), new JSComponentConfigurationStore(), "index.html")
        {
            _dispatcher = dispatcher;
            _dispatcher.Unhandled = Unhandled;
        }

        public int CountSelector(string selector) =>
            Messages.Count(message => message.Contains($"\"{selector}\"", StringComparison.Ordinal));

        public async Task AttachPageAsync()
        {
            var before = _dispatcher.Tasks.Count;
            MessageReceived(new Uri(AppOrigin + "index.html"), "__bwv:[\"AttachPage\",\"https://0.0.0.1/\",\"https://0.0.0.1/\"]");
            var pending = _dispatcher.Tasks.Skip(before).ToArray();
            await Task.WhenAll(pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            foreach (var task in pending)
            {
                if (task.Exception is null)
                    continue;
                foreach (var ex in Flatten(task.Exception))
                    Unhandled.Add(ex);
            }
        }

        protected override void NavigateCore(Uri absoluteUri)
        {
        }

        protected override void SendMessage(string message)
        {
            Messages.Add(message);
            if (!message.StartsWith("__bwv:", StringComparison.Ordinal))
                return;

            using var document = JsonDocument.Parse(message["__bwv:".Length..]);
            var root = document.RootElement;
            if (root.GetArrayLength() < 2 || root[0].GetString() != "RenderBatch")
                return;

            var batchId = root[1].GetInt64();
            MessageReceived(new Uri(AppOrigin + "index.html"), $"__bwv:[\"OnRenderCompleted\",{batchId},null]");
        }
    }

    private sealed class InlineDispatcher : Dispatcher
    {
        public List<Task> Tasks { get; } = [];
        public List<Exception> Unhandled { get; set; } = [];

        public override bool CheckAccess() => true;

        public override Task InvokeAsync(Action workItem)
        {
            try
            {
                workItem();
                var task = Task.CompletedTask;
                Tasks.Add(task);
                return task;
            }
            catch (Exception ex)
            {
                Unhandled.Add(ex);
                var task = Task.FromException(ex);
                Tasks.Add(task);
                return task;
            }
        }

        public override Task InvokeAsync(Func<Task> workItem)
        {
            try
            {
                var task = workItem();
                Tasks.Add(task);
                return task;
            }
            catch (Exception ex)
            {
                Unhandled.Add(ex);
                var task = Task.FromException(ex);
                Tasks.Add(task);
                return task;
            }
        }

        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
        {
            try
            {
                var task = Task.FromResult(workItem());
                Tasks.Add(task);
                return task;
            }
            catch (Exception ex)
            {
                Unhandled.Add(ex);
                var task = Task.FromException<TResult>(ex);
                Tasks.Add(task);
                return task;
            }
        }

        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
        {
            try
            {
                var task = workItem();
                Tasks.Add(task);
                return task;
            }
            catch (Exception ex)
            {
                Unhandled.Add(ex);
                var task = Task.FromException<TResult>(ex);
                Tasks.Add(task);
                return task;
            }
        }
    }

    private sealed class ConnectedClient : IAsyncDisposable
    {
        private readonly IpcHost _host;
        private readonly string _id;
        private readonly TaskCompletionSource<ClientRemote.ConnectedEventArgs> _connected;
        private readonly TaskCompletionSource<ClientRemote.DisconnectedEventArgs> _disconnected;

        public ClientRemote.RemoteWebView Remote { get; }

        private ConnectedClient(
            IpcHost host,
            string id,
            ClientRemote.RemoteWebView remote,
            TaskCompletionSource<ClientRemote.ConnectedEventArgs> connected,
            TaskCompletionSource<ClientRemote.DisconnectedEventArgs> disconnected)
        {
            _host = host;
            _id = id;
            Remote = remote;
            _connected = connected;
            _disconnected = disconnected;
        }

        public bool ChannelDisposed
        {
            get
            {
                var channel = Channel();
                var disposed = typeof(GrpcChannel).GetProperty("Disposed", BindingFlags.Instance | BindingFlags.NonPublic);
                return disposed?.GetValue(channel) is true || channel.State == ConnectivityState.Shutdown;
            }
        }

        public static async Task<ConnectedClient> StartAsync(IpcHost host)
        {
            var id = Guid.NewGuid();
            var connected = new TaskCompletionSource<ClientRemote.ConnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            var disconnected = new TaskCompletionSource<ClientRemote.DisconnectedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            var view = new FakeBlazorWebView(id, host.Http1Url, host.Http2Url, connected, disconnected);
            var remote = new ClientRemote.RemoteWebView(
                view,
                "wwwroot/index.html",
                new InlineDispatcher(),
                new NullFileProvider(),
                NullLogger.Instance);

            var started = Task.Run(remote.Initialize);
            var finished = await Task.WhenAny(started, Task.Delay(TimeSpan.FromSeconds(20)));
            if (finished != started)
                throw new TimeoutException($"Desktop client did not finish CreateWebView. Http1={host.Http1Url} Http2={host.Http2Url}");
            await started;
            return new ConnectedClient(host, id.ToString(), remote, connected, disconnected);
        }

        public async Task SignalConnectedAsync()
        {
            var state = await _host.RequireStateAsync(_id);
            await state.IPC.ClientResponseStream!.WriteAsync(new ServiceIpc.WebMessageResponse
            {
                Response = "connected:127.0.0.1|tester",
                Cookies = "{}"
            });
            await _connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async Task SignalShutdownAsync()
        {
            var state = await _host.RequireStateAsync(_id);
            await state.IPC.ClientResponseStream!.WriteAsync(new ServiceIpc.WebMessageResponse { Response = "shutdown:" });
            await _disconnected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await Remote.DisposeAsyncCore();
            }
            catch (Exception)
            {
            }
        }

        private GrpcChannel Channel()
        {
            var field = typeof(ClientRemote.RemoteWebView).GetField("channel", BindingFlags.Instance | BindingFlags.NonPublic);
            var channel = field?.GetValue(Remote) as GrpcChannel;
            Assert.NotNull(channel);
            return channel!;
        }
    }

    private sealed class FakeBlazorWebView : ClientRemote.IBlazorWebView
    {
        private readonly TaskCompletionSource<ClientRemote.ConnectedEventArgs> _connected;
        private readonly TaskCompletionSource<ClientRemote.DisconnectedEventArgs> _disconnected;

        public FakeBlazorWebView(
            Guid id,
            Uri serverUri,
            Uri grpcBaseUri,
            TaskCompletionSource<ClientRemote.ConnectedEventArgs> connected,
            TaskCompletionSource<ClientRemote.DisconnectedEventArgs> disconnected)
        {
            Id = id;
            ServerUri = serverUri;
            GrpcBaseUri = grpcBaseUri;
            _connected = connected;
            _disconnected = disconnected;
        }

        public event EventHandler<ClientRemote.ConnectedEventArgs>? Connected;
        public event EventHandler<ClientRemote.DisconnectedEventArgs>? Disconnected;
        public event EventHandler<ClientRemote.RefreshedEventArgs>? Refreshed;
        public event EventHandler<ClientRemote.ReadyToConnectEventArgs>? ReadyToConnect;

        public Uri? ServerUri { get; set; }
        public Uri? GrpcBaseUri { get; set; }
        public string Group { get; set; } = "";
        public Guid Id { get; set; }
        public bool EnableMirrors { get; set; }
        public uint PingIntervalSeconds { get; set; } = 60;
        public string Markup { get; set; } = "";
        public Func<ClientRemote.BlazorWebViewStaticContentRequest, string?>? StaticContentCacheControlProvider { get; set; }

        public CoreWebView2CookieManager CookieManager =>
            throw new NotSupportedException("This test sends an empty cookie map, so the cookie manager is not used.");

        public Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri) => Task.FromResult(GrpcBaseUri);
        public void Restart() { }
        public void NavigateToString(string htmlContent) { }
        public Task WaitForInitializationComplete() => Task.CompletedTask;

        public void FireConnected(ClientRemote.ConnectedEventArgs args)
        {
            Connected?.Invoke(this, args);
            _connected.TrySetResult(args);
        }

        public void FireDisconnected(ClientRemote.DisconnectedEventArgs args)
        {
            Disconnected?.Invoke(this, args);
            _disconnected.TrySetResult(args);
        }

        public void FireRefreshed(ClientRemote.RefreshedEventArgs args) => Refreshed?.Invoke(this, args);
        public void FireReadyToConnect(ClientRemote.ReadyToConnectEventArgs args) => ReadyToConnect?.Invoke(this, args);
    }

    private sealed class IpcHost : IAsyncDisposable
    {
        private readonly WebApplication _health;
        private readonly WebApplication _grpc;
        public Uri Http1Url { get; }
        public Uri Http2Url { get; }
        public ConcurrentDictionary<string, TaskCompletionSource<ServiceIpc.ServiceState>> States { get; }

        private IpcHost(
            WebApplication health,
            WebApplication grpc,
            Uri http1,
            Uri http2,
            ConcurrentDictionary<string, TaskCompletionSource<ServiceIpc.ServiceState>> states)
        {
            _health = health;
            _grpc = grpc;
            Http1Url = http1;
            Http2Url = http2;
            States = states;
        }

        public static async Task<IpcHost> StartAsync()
        {
            // RemoteWebView's health probe is an HTTP/1.1 GET. .NET 11 cleartext Kestrel will not
            // negotiate HTTP/2 on an Http1AndHttp2 endpoint, so gRPC gets its own HTTP/2 listener.
            var healthBuilder = WebApplication.CreateBuilder();
            healthBuilder.WebHost.UseUrls("http://127.0.0.1:0");
            healthBuilder.Logging.SetMinimumLevel(LogLevel.Warning);
            var health = healthBuilder.Build();
            health.MapGet("/health", () => "OK");
            await health.StartAsync();

            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceIpc.ServiceState>>();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2);
            });
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.Services.AddGrpc();
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton(states);
            builder.Services.AddSingleton(new ConcurrentDictionary<string, Channel<string>>());
            builder.Services.AddSingleton(new RemoteFilesOptions { UseClientCache = false, UseServerCache = false });
            builder.Services.AddSingleton<ServerFileSyncManager>();
            builder.Services.AddSingleton<RemoteFileResolver>();
            builder.Services.AddSingleton<ServiceIpc.Services.ShutdownService>();
            builder.Services.AddSingleton<ServiceIpc.RemoteWebViewService>();
            var grpc = builder.Build();
            grpc.MapGrpcService<ServiceIpc.RemoteWebViewService>();
            await grpc.StartAsync();

            var http1 = new Uri(health.Urls.Single().TrimEnd('/') + "/");
            var http2 = new Uri(grpc.Urls.Single().TrimEnd('/') + "/");
            return new IpcHost(health, grpc, http1, http2, states);
        }

        public async Task<ServiceIpc.ServiceState> RequireStateAsync(string id)
        {
            if (!States.TryGetValue(id, out var source))
                throw new InvalidOperationException($"No service state for {id}.");
            return await source.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        public async ValueTask DisposeAsync()
        {
            await _grpc.DisposeAsync();
            await _health.DisposeAsync();
        }
    }
}
