using Microsoft.AspNetCore.Components;
using Microsoft.Web.WebView2.Core;
using PeakSWC.RemoteWebView;
using Photino.NET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class RemoteBlazorWebViewWindow : PhotinoWindow, IBlazorWebView
    {
        /// <summary>
        /// The page a remote app shows until ReadyToConnect replaces it. Its script reports that WebView2 is up.
        /// </summary>
        internal const string StartPage = "<br/><script>window.external.sendMessage('rbwv:ready')</script>";

        // Set by the first message from any page (StartPage's report, or a local
        // Blazor page's own traffic), which proves WebView2 has finished starting.
        private readonly TaskCompletionSource webViewReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RemoteBlazorWebViewWindow()
        {
            WebMessageReceived += (_, _) => webViewReady.TrySetResult();
        }

        public CoreWebView2CookieManager CookieManager => default!;
        public Uri? ServerUri { get; set; }
        public string Group { get; set; } = "test";
        public string Markup { get; set; } = "";

        /// <summary>
        /// Secret the relay uses to recognise this app's own owner page (/app/{id}?role=owner&amp;key=...). Set it, and open that
        /// page with it, for a shared session; the source generator does both for ShareControl. Empty: ?role=owner is trusted.
        /// </summary>
        public string OwnerKey { get; set; } = string.Empty;

        public bool EnableMirrors { get; set; } = false;

        private Guid id = Guid.Empty;
        public new Guid Id
        {
            get
            {
                if (id == Guid.Empty)
                    throw new Exception("Id not initialized");

                return id;
            }
            set
            {
                if (value == Guid.Empty)
                    id = Guid.NewGuid();
                else
                    id = value;
            }
        }

        public Uri? GrpcBaseUri { get; set; }
        public uint PingIntervalSeconds { get; set; } = 30;

        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;
        public event EventHandler<ReadyToConnectEventArgs>? ReadyToConnect;

        public void FireConnected(ConnectedEventArgs args)
        {
            Invoke(() => Connected?.Invoke(this, args));
        }

        public void FireDisconnected(DisconnectedEventArgs args)
        {
            Invoke(() => Disconnected?.Invoke(this, args));
        }

        public void FireRefreshed(RefreshedEventArgs args)
        {
            Invoke(() => Refreshed?.Invoke(this, args));
        }

        public void FireReadyToConnect(ReadyToConnectEventArgs args)
        {
            Invoke(() => ReadyToConnect?.Invoke(this, args));
        }

        public void FireParticipantsChanged(ParticipantsChangedEventArgs args)
        {
            Invoke(() => ParticipantsChanged?.Invoke(this, args));
        }

        /// <summary>Someone joined or left the shared session, or control moved. Needs relay 10.0.15 or later.</summary>
        public event EventHandler<ParticipantsChangedEventArgs>? ParticipantsChanged;

        /// <inheritdoc cref="IBlazorWebView.RestartWhenUnresponsive"/>
        public Action? RestartWhenUnresponsive { get; set; }

        public void Restart()
        {
            RemoteWebView.Restart(this);
        }

        public void NavigateToString(string htmlContent)
        {
            // TODO Need to wait for window????
             this.LoadRawString(htmlContent);
        }

        /// <summary>
        /// Waits until WebView2 has finished starting, so a ReadyToConnect handler can call NavigateToString.
        /// Photino's native NavigateToString crashes (0xC0000005) before then. A fixed 5s wait after the window
        /// handle used to stand in for this, and a build's first start on a fresh PC can take longer.
        /// </summary>
        public async Task WaitForInitializationComplete()
        {
            var done = await Task.WhenAny(webViewReady.Task, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
            if (done != webViewReady.Task)
                Debug.WriteLine("WebView2 did not report ready within 60s; continuing.");
        }

        public Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri)
        {
            return RemoteWebView.GetGrpcBaseUriAsync(serverUri);
        }
    }
}
