using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace PeakSWC.RemoteWebView
{
    public class RemoteWebView2Manager : WebView2WebViewManager
    {
        Uri url;
        private RemoteWebView RemoteWebView { get; }
        private IBlazorWebView BlazorWebView { get; }
        public RemoteWebView2Manager(IBlazorWebView blazorWebView, IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath) : base(webview, services, dispatcher, fileProvider,store, hostPageRelativePath)
        {
            BlazorWebView = blazorWebView;
            RemoteWebView = new RemoteWebView(
                blazorWebView,
                hostPageRelativePath,
                dispatcher,
                new CompositeFileProvider(StaticWebAssetsLoader.UseStaticWebAssets(fileProvider), new EmbeddedFileProvider(Assembly.GetExecutingAssembly()))
                );

            RemoteWebView.OnWebMessageReceived += RemoteOnWebMessageReceived;
            RemoteWebView.Initialize();
            
            this.url = new Uri("https://0.0.0.0/");
        }
        private void RemoteOnWebMessageReceived(object? sender, string e)
        {
            this.Dispatcher.InvokeAsync(() =>
            {
                var url = sender?.ToString() ?? "";
                if (BlazorWebView.ServerUri != null && url.StartsWith(BlazorWebView.ServerUri.ToString()))
                {
                    url = url.Replace(BlazorWebView.ServerUri.ToString(), this.url?.ToString() ?? "");
                    url = url.Replace(BlazorWebView.Id.ToString() + $"/", "");
                    if (url.EndsWith(RemoteWebView.HostHtmlPath)) url = url.Replace(RemoteWebView.HostHtmlPath, "");
                }

                MessageReceived(new Uri(url), e);
            });
           
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
