using Microsoft.AspNetCore.Components;
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
        RemotableWebWindow? remoteableWebView;
        Uri? url;

        public RemoteWebView2Manager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath, Uri? url, Guid id) : base(webview, services, dispatcher, fileProvider, hostPageRelativePath)
        {
            if (url != null)
            {
                //this.url = url;
                remoteableWebView = new RemotableWebWindow(url, hostPageRelativePath, id);
                remoteableWebView.OnWebMessageReceived += RemoteOnWebMessageReceived;
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
