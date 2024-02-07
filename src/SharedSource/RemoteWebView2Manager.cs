//using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
//using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using PeakSWC.RemoteBlazorWebView;
using Microsoft.Extensions.Logging;
#if WEBVIEW2_WINFORMS
using Microsoft.Web.WebView2;
using Microsoft.Web.WebView2.Core;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using WebView2Control = Microsoft.Web.WebView2.WinForms.WebView2;
//using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components;
#elif WEBVIEW2_WPF
using Microsoft.Web.WebView2;
using Microsoft.Web.WebView2.Core;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;
using Microsoft.AspNetCore.Components;
//using Microsoft.AspNetCore.Components.WebView;
#endif 

namespace PeakSWC.RemoteWebView
{
    public class RemoteWebView2Manager : PeakSWC.RemoteBlazorWebView.WebView2WebViewManager
    {
        Uri url;
        private RemoteWebView RemoteWebView { get; }
        private IBlazorWebView BlazorWebView { get; }
        public RemoteWebView2Manager(IBlazorWebView blazorWebView, WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath, string hostPagePathWithinFileProvider, Action<UrlLoadingEventArgs> externalNavigationStarting, Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing, Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized, ILogger logger) : base(webview, services, dispatcher, fileProvider,store, hostPageRelativePath, hostPagePathWithinFileProvider, externalNavigationStarting, blazorWebViewInitializing, blazorWebViewInitialized, logger)
        {
            BlazorWebView = blazorWebView;
            RemoteWebView = new RemoteWebView(
                blazorWebView,
                hostPageRelativePath + "//" + hostPagePathWithinFileProvider,
                dispatcher,
                new CompositeFileProvider(StaticWebAssetsLoader.UseStaticWebAssets(fileProvider), new EmbeddedFileProvider(typeof(RemoteWebView).Assembly))
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
                var tempServerUri = BlazorWebView.ServerUri?.ToString() ?? "";

                // Check if the ServerUri has a port and ends with '0'
                var uri = new Uri(tempServerUri);
                if (uri.Port % 10 == 0)
                {
                    // Reconstruct the URI without the last '0' in the port
                    var port = uri.Port / 10; // Remove the last '0'
                    var host = uri.Host;
                    tempServerUri = $"{uri.Scheme}://{host}:{port}";
                }

                if (tempServerUri != null && url.StartsWith(tempServerUri.ToString()))
                {
                    url = url.Replace(tempServerUri.ToString(), this.url?.ToString() ?? "");
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
