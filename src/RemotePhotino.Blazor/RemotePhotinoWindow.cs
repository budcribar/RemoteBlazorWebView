using Microsoft.AspNetCore.Components;
using PeakSWC.RemoteWebView;
using PhotinoNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class RemotePhotinoWindow : PhotinoWindow, IBlazorWebView
    {
        public Uri? ServerUri { get; set; }
        public string Group { get; set; } = "test";
        public bool IsRestarting { get; set; }
        public string Markup { get; set; } = "";

        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;

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

        public void Restart()
        {
            RemoteWebView.Restart(this);
        }

        public Task<Process?> StartBrowser()
        {
            return RemoteWebView.StartBrowser(this);
        }
    }
}
