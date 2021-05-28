using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSwc.RemoteableWebWindows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC
{
    public class RemoteWebView2Manager : WebView2WebViewManager
    {
        public RemotableWebWindow? remoteableWebView { get; set; } 
        Uri? url;
       
        public RemoteWebView2Manager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath, Uri? url, Guid id) : base(webview, services, dispatcher, fileProvider, hostPageRelativePath)
        {
            if (url != null)
            {
               
                remoteableWebView = new RemotableWebWindow();
                remoteableWebView.uri = url;
                remoteableWebView.hostHtmlPath = hostPageRelativePath;
                remoteableWebView.Id = id == default(Guid) ? Guid.NewGuid().ToString() : id.ToString();
                remoteableWebView.OnWebMessageReceived += RemoteOnWebMessageReceived;
                remoteableWebView.Initialize();
                remoteableWebView.Dispacher = dispatcher;
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

            if (remoteableWebView == null)
                base.NavigateCore(absoluteUri);
            else
                remoteableWebView.NavigateToUrl(absoluteUri.AbsoluteUri); 
        }

        protected override void SendMessage(string message)
        {
            if (remoteableWebView == null)
                base.SendMessage(message);
            else
                remoteableWebView.SendMessage(message);
        }
    }
}
