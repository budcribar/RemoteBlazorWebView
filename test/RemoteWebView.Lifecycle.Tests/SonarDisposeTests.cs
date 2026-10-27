extern alias client;

using System.Reflection;
using System.Threading;
using ClientRemote = client::PeakSWC.RemoteWebView;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;

namespace RemoteWebView.Lifecycle.Tests;

public class SonarDisposeTests
{
    [Fact]
    public async Task Dispose_releases_the_cancellation_source_and_a_second_dispose_does_not_throw()
    {
        var view = new ClientRemote.RemoteWebView(
            new UnusedBlazorWebView(),
            "index.html",
            Dispatcher.CreateDefault(),
            new NullFileProvider(),
            NullLogger.Instance);
        var cts = (CancellationTokenSource)typeof(ClientRemote.RemoteWebView)
            .GetField("cts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(view)!;
        var pingStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cts.Token.Register(() => pingStopped.TrySetResult());

        await view.DisposeAsyncCore();

        Assert.True(pingStopped.Task.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => _ = cts.Token);
        await view.DisposeAsyncCore();
    }

    private sealed class UnusedBlazorWebView : ClientRemote.IBlazorWebView
    {
        public event EventHandler<ClientRemote.ConnectedEventArgs>? Connected;
        public event EventHandler<ClientRemote.DisconnectedEventArgs>? Disconnected;
        public event EventHandler<ClientRemote.RefreshedEventArgs>? Refreshed;
        public event EventHandler<ClientRemote.ReadyToConnectEventArgs>? ReadyToConnect;
        public void FireConnected(ClientRemote.ConnectedEventArgs args) => Connected?.Invoke(this, args);
        public void FireDisconnected(ClientRemote.DisconnectedEventArgs args) => Disconnected?.Invoke(this, args);
        public void FireRefreshed(ClientRemote.RefreshedEventArgs args) => Refreshed?.Invoke(this, args);
        public void FireReadyToConnect(ClientRemote.ReadyToConnectEventArgs args) => ReadyToConnect?.Invoke(this, args);
        public Uri? ServerUri { get; set; }
        public Uri? GrpcBaseUri { get; set; }
        public Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri) => Task.FromResult(serverUri);
        public string Group { get; set; } = "";
        public Guid Id { get; set; }
        public bool EnableMirrors { get; set; }
        public uint PingIntervalSeconds { get; set; }
        public string Markup { get; set; } = "";
        public Func<ClientRemote.BlazorWebViewStaticContentRequest, string?>? StaticContentCacheControlProvider { get; set; }
        public void Restart() { }
        public void NavigateToString(string htmlContent) { }
        public Task WaitForInitializationComplete() => Task.CompletedTask;
        public CoreWebView2CookieManager CookieManager => null!;
    }
}
