using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using System;
using System.Reflection;

namespace PeakSWC.RemoteWebView
{
    public class RemoteWebView2Manager : WebView2WebViewManager
    {
        Uri url;
        public RemoteWebView RemoteWebView { get; set; }
       
        public RemoteWebView2Manager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath, Uri url, string id, string group, string markup) : base(webview, services, dispatcher, fileProvider,store, hostPageRelativePath)
        {
            RemoteWebView = new RemoteWebView(
                url,
                hostPageRelativePath,
                dispatcher,
                new CompositeFileProvider(StaticWebAssetsLoader.UseStaticWebAssets(fileProvider), new EmbeddedFileProvider(Assembly.GetExecutingAssembly())),
                id,
                group,
                markup
                );

            RemoteWebView.OnWebMessageReceived += RemoteOnWebMessageReceived;
            RemoteWebView.Initialize();
            this.url = new Uri("https://0.0.0.0/");
        }
        private void RemoteOnWebMessageReceived(object? sender, string e)
        {
            var url = sender?.ToString() ?? "";
            if (RemoteWebView.ServerUri != null && url.StartsWith(RemoteWebView.ServerUri.ToString()))
            {
                url = url.Replace(RemoteWebView.ServerUri.ToString(), this.url?.ToString() ?? "");
                url = url.Replace(RemoteWebView.Id.ToString() + $"/", "");
                if (url.EndsWith(RemoteWebView.HostHtmlPath)) url = url.Replace(RemoteWebView.HostHtmlPath, "");             
            }
            
            MessageReceived(new Uri(url), e);
        }

        protected override void NavigateCore(Uri absoluteUri)
        {
            this.url = absoluteUri;
            RemoteWebView.NavigateToUrl(absoluteUri.AbsoluteUri);
        }

        protected override void SendMessage(string message)
        {
            RemoteWebView.SendMessage(message);
        }
    }
}
