using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace PeakSWC.RemoteWebView
{
    public interface IBlazorWebView
    {
        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;
        public event EventHandler<ReadyToConnectEventArgs>? ReadyToConnect;
        public void FireConnected(ConnectedEventArgs args);
        public void FireDisconnected(DisconnectedEventArgs args);
        public void FireRefreshed(RefreshedEventArgs args);
        public void FireReadyToConnect(ReadyToConnectEventArgs args);
        public Uri? ServerUri { get; set; }

        public Uri? GrpcBaseUri { get; set; }

        public Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri);
        public string Group { get; set; }
        public Guid Id { get; set; }
        public bool EnableMirrors { get; set; }

        public uint PingIntervalSeconds { get; set; }

        public string Markup { get; set; }

        /// <summary>
        /// Same contract as MAUI <c>BlazorWebView.StaticContentCacheControlProvider</c>.
        /// The callback receives the app-relative request URI and the content type the remote service will send.
        /// A null provider leaves the existing remote cache headers unchanged. A provider that returns null,
        /// empty, or whitespace selects <c>no-cache, max-age=0, must-revalidate, no-store</c>.
        /// A value longer than 1024 characters, or one that contains a carriage return or line feed, also selects that default.
        /// </summary>
        Func<BlazorWebViewStaticContentRequest, string?>? StaticContentCacheControlProvider { get; set; }

        public void Restart();
        
        public void NavigateToString(string htmlContent);

        public Task WaitForInitializationComplete();

        public CoreWebView2CookieManager CookieManager {get;}
    }
}
