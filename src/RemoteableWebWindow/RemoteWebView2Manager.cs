using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using System;

namespace PeakSWC.RemoteableWebView
{
    public class RemoteWebView2Manager : WebView2WebViewManager, IWebViewManager
    {
        public RemotableWebWindow? RemoteableWebView { get; set; }
        Uri? url;

        public RemoteWebView2Manager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath, Uri? url, Guid id) : base(webview, services, dispatcher, fileProvider, hostPageRelativePath)
        {
            if (url != null)
            {
                RemoteableWebView = new RemotableWebWindow
                {
                    ServerUri = url,
                    HostHtmlPath = hostPageRelativePath,
                    Id = id == default ? Guid.NewGuid().ToString() : id.ToString(),
                    Dispacher = dispatcher
                };
                RemoteableWebView.OnWebMessageReceived += RemoteOnWebMessageReceived;
                RemoteableWebView.Initialize();
            }
        }

        private void RemoteOnWebMessageReceived(object? sender, string e)
        {
            if (url != null)
                MessageReceived(url, e);
        }

        protected override void NavigateCore(Uri absoluteUri)
        {
            this.url = absoluteUri;

            if (RemoteableWebView == null)
                base.NavigateCore(absoluteUri);
            else
                RemoteableWebView.NavigateToUrl(absoluteUri.AbsoluteUri);
        }

        protected override void SendMessage(string message)
        {
            if (RemoteableWebView == null)
                base.SendMessage(message);
            else
                RemoteableWebView.SendMessage(message);
        }
    }
}
