using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using System;
using System.Reflection;

namespace PeakSWC.RemoteableWebView
{
    public class RemoteWebView2Manager : WebView2WebViewManager, IWebViewManager
    {
        Uri url;
        public RemoteableWebView RemoteableWebView { get; set; }
       
        public RemoteWebView2Manager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath, Uri url, string id, string group, string markup) : base(webview, services, dispatcher, fileProvider, hostPageRelativePath)
        {
            RemoteableWebView = new RemoteableWebView(
                url,
                hostPageRelativePath,
                dispatcher,
                new CompositeFileProvider(StaticWebAssetsLoader.UseStaticWebAssets(fileProvider), new EmbeddedFileProvider(Assembly.GetExecutingAssembly())),
                id,
                group,
                markup
                );

            RemoteableWebView.OnWebMessageReceived += RemoteOnWebMessageReceived;
            RemoteableWebView.Initialize();
            this.url = new Uri("https://0.0.0.0/");
        }
        private void RemoteOnWebMessageReceived(object? sender, string e)
        {
            var url = sender?.ToString() ?? "";
            if (RemoteableWebView.ServerUri != null && url.StartsWith(RemoteableWebView.ServerUri.ToString()))
            {
                url = url.Replace(RemoteableWebView.ServerUri.ToString(), this.url?.ToString() ?? "");
                url = url.Replace(RemoteableWebView.Id.ToString() + $"/", "");
                if (url.EndsWith(RemoteableWebView.HostHtmlPath)) url = url.Replace(RemoteableWebView.HostHtmlPath, "");             
            }
            
            MessageReceived(new Uri(url), e);
        }

        protected override void NavigateCore(Uri absoluteUri)
        {
            this.url = absoluteUri;
            RemoteableWebView.NavigateToUrl(absoluteUri.AbsoluteUri);
        }

        protected override void SendMessage(string message)
        {
            RemoteableWebView.SendMessage(message);
        }
    }
}
