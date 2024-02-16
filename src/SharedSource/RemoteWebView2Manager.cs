//using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
//using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using PeakSWC.RemoteBlazorWebView;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

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
        private WebView2Control webview2Control { get; }

        private async Task WaitForNavigationAsync(CoreWebView2 webView, string url)
        {
            var navigationCompletedTcs = new TaskCompletionSource<bool>();

            EventHandler<CoreWebView2NavigationCompletedEventArgs> handler = null;
            handler = (sender, args) =>
            {
                // Remove the handler since navigation is completed
                webView.NavigationCompleted -= handler;

                // Set the task as completed
                navigationCompletedTcs.SetResult(true);
            };

            webView.NavigationCompleted += handler;

            try
            {
                webView.Navigate(url);

                await navigationCompletedTcs.Task;
            }
            catch (Exception)
            {
                webView.NavigationCompleted -= handler;
                throw;
            }
        }

        public async Task Shutdown()
        {
            if (webview2Control.CoreWebView2 != null)
            {
                // Navigate to a blank page to ensure the release of resources
                await WaitForNavigationAsync(webview2Control.CoreWebView2, "about:blank");
                // Wait for the navigation to complete, if necessary. Consider using an event or a delay.
                // System.Threading.Thread.Sleep(1000); // Use with caution, just for demonstration.

                // Dispose of the WebView2 control
                webview2Control.Dispose();
            }
        }
        public RemoteWebView2Manager(IBlazorWebView blazorWebView, WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath, string hostPagePathWithinFileProvider, Action<UrlLoadingEventArgs> externalNavigationStarting, Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing, Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized, ILogger logger) : base(webview, services, dispatcher, fileProvider,store, hostPageRelativePath, hostPagePathWithinFileProvider, externalNavigationStarting, blazorWebViewInitializing, blazorWebViewInitialized,logger)
        {
            webview2Control = webview;
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
