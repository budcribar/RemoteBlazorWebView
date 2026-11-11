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
        /// <summary>Raises ParticipantsChanged on the control. The default does nothing (controls without the event).</summary>
        public void FireParticipantsChanged(ParticipantsChangedEventArgs args) { }
        public Uri? ServerUri { get; set; }

        public Uri? GrpcBaseUri { get; set; }

        public Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri);
        public string Group { get; set; }
        public Guid Id { get; set; }
        public bool EnableMirrors { get; set; }

        /// <summary>Secret for this app's own owner page; sent to the relay with CreateWebView. Empty: none (see OwnerAccess on the relay).</summary>
        public string OwnerKey { get => string.Empty; set { } }

        public uint PingIntervalSeconds { get; set; }

        public string Markup { get; set; }
        public void Restart();

        /// <summary>
        /// Called on a background thread when a restart was requested (the Refreshed event) but the UI thread did not
        /// respond within a few seconds, so Refreshed could not be raised. Set it to restart the app without the UI
        /// thread: start the new copy, then end this process (a window cannot be closed from here). Unset (the default),
        /// a restart request to an app whose UI thread is stuck does nothing.
        /// </summary>
        public Action? RestartWhenUnresponsive { get => null; set { } }
        
        public void NavigateToString(string htmlContent);

        public Task WaitForInitializationComplete();

        public CoreWebView2CookieManager CookieManager {get;}
    }
}
