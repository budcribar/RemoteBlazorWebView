using System;

namespace RemoteBlazorWebView.Wpf
{
    public interface IBlazorWebView 
    {
        public event EventHandler<string> Unloaded;
        public event EventHandler<string> Loaded;

        public Uri? ServerUri { get; set; }
        public Guid Id { get; set; }
        public bool IsRestarting { get; set; }
        public void Restart();
        public void StartBrowser();
    }
}
