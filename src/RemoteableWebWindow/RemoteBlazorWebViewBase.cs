//using RemoteBlazorWebView.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSwc.RemoteableWebWindows;

namespace PeakSWC
{
    public class RemoteBlazorWebViewBase : BlazorWebViewBaseWpf
    {
        public Uri? ServerUri { get; set; }
        public Guid Id { get; set; }
        public IWebViewManager? WebViewManager { get; set; }

        public override IWebViewManager CreateWebViewManager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath)
        {
            WebViewManager = new RemoteWebView2Manager(webview, services, dispatcher, fileProvider, hostPageRelativePath, ServerUri, Id);
            return WebViewManager;

        }

    }
}
