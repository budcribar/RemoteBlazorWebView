using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public interface IBlazorWebView
    {
        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;

        public void FireConnected(ConnectedEventArgs args);
        public void FireDisconnected(DisconnectedEventArgs args);
        public void FireRefreshed(RefreshedEventArgs args);
        public Uri? ServerUri { get; set; }
        public string Group { get; set; }
        public bool IsRestarting { get; set; }
        public Guid Id { get; }
        public void Restart();
        public Task<Process?> StartBrowser();
    }
}
