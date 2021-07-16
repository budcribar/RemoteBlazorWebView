using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSWC.RemoteableWebView;
using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace PeakSWC.RemoteBlazorWebView.Wpf
{

    public class BlazorWebView : Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView, IBlazorWebView
    {
        #region Properties

        public static readonly DependencyProperty UriProperty = DependencyProperty.Register(
            name: nameof(ServerUri),
            propertyType: typeof(Uri),
            ownerType: typeof(BlazorWebView),
            typeMetadata: new PropertyMetadata(OnServerUriPropertyChanged));

        public static readonly DependencyProperty IdProperty = DependencyProperty.Register(
                   name: nameof(Id),
                   propertyType: typeof(Guid),
                   ownerType: typeof(BlazorWebView),
                   typeMetadata: new PropertyMetadata(OnIdPropertyChanged));
        #endregion

        public Uri? ServerUri
        {
            get => (Uri?)GetValue(UriProperty);
            set => SetValue(UriProperty, value);
        }
        public Guid Id
        {
            get => (Guid)GetValue(IdProperty);
            set => SetValue(IdProperty, value == Guid.Empty ? Guid.NewGuid() : value);
        }


        public bool IsRestarting
        {
            get { return (bool)GetValue(IsRestartingProperty); }
            set { SetValue(IsRestartingProperty, value); }
        }

        public static readonly DependencyProperty IsRestartingProperty =
            DependencyProperty.Register(nameof(IsRestarting), typeof(bool), typeof(BlazorWebView), new PropertyMetadata(false));

        private static void OnServerUriPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnServerUriPropertyChanged(e);

        private void OnServerUriPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private static void OnIdPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnIdPropertyChanged(e);

        private void OnIdPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        public WebView2WebViewManager? WebViewManager { get; set; }

        public override WebView2WebViewManager CreateWebViewManager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher)
        {
            // We assume the host page is always in the root of the content directory, because it's
            // unclear there's any other use case. We can add more options later if so.
            var contentRootDir = Path.GetDirectoryName(Path.GetFullPath(HostPage));
            if (contentRootDir == null) throw new Exception("No root directory found");
            var hostPageRelativePath = Path.GetRelativePath(contentRootDir, HostPage);

            // TODO Extract to method
            IFileProvider provider;

            var root = Path.GetDirectoryName(HostPage) ?? string.Empty;
            try
            {
                EmbeddedFilesManifest manifest = ManifestParser.Parse(new FixedManifestEmbeddedAssembly(Assembly.GetEntryAssembly()!));
                var dir = manifest._rootDirectory.Children.Where(x => (x as ManifestDirectory)?.Children.Any(y => y.Name == root) ?? false).FirstOrDefault();

                if (dir != null)
                {
                    var manifestRoot = Path.Combine(dir.Name, root);
                    provider = new ManifestEmbeddedFileProvider(new FixedManifestEmbeddedAssembly(Assembly.GetEntryAssembly()!), manifestRoot);
                }
                else provider = new PhysicalFileProvider(contentRootDir);
            }
            catch (Exception) { provider = new PhysicalFileProvider(contentRootDir); }

            if (ServerUri == null)
                WebViewManager = new WebView2WebViewManager(webview, services, dispatcher, provider, hostPageRelativePath);
            else
                WebViewManager = new RemoteWebView2Manager(webview, services, dispatcher, provider, hostPageRelativePath, ServerUri, Id);

            return WebViewManager;
        }

        public new event EventHandler<string> Unloaded
        {
            add
            {
                if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteableWebView != null)
                    manager.RemoteableWebView.OnDisconnected += value;
                //else
                //    MainBlazorWebView.Unloaded +=  value;
            }

            remove
            {
                if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteableWebView != null)
                    manager.RemoteableWebView.OnDisconnected -= value;
                //else
                //    MainBlazorWebView.Unloaded -= value;
            }
        }

        public new event EventHandler<string> Loaded
        {
            add
            {
                if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteableWebView != null)
                    manager.RemoteableWebView.OnConnected += value;
                //else
                //MainBlazorWebView.Loaded += value;
            }

            remove
            {
                if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteableWebView != null)
                    manager.RemoteableWebView.OnConnected -= value;
                //else
                //    MainBlazorWebView.Loaded -= value;
            }
        }

        private void HandleRootComponentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            Services = this.Services;
            RootComponents.ToList().ForEach(x => RootComponents.Add(x));
            HostPage = HostPage;

            if (ServerUri != null)
                Id = Id;

        }


        public void Restart()
        {
            RemotableWebWindow.Restart(this);
        }

        public void StartBrowser()
        {
            RemotableWebWindow.StartBrowser(this);
        }
    }
}
