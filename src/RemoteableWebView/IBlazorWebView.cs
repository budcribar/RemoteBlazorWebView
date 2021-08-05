using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PeakSWC.RemoteableWebView
{
    public interface IBlazorWebView
    {
        public event EventHandler<string> Unloaded;
        public event EventHandler<string> Loaded;

        public Uri? ServerUri { get; set; }
        public string Group { get; set; }
        public bool IsRestarting { get; set; }
        public Guid Id { get; }
        public void Restart();
        public Task<Process?> StartBrowser();
    }
}
