using PeakSWC.RemoteableWebView;
using System;
using System.Linq;
using System.Windows;
using System.Collections.Specialized;
using PeakSWC;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Components;

namespace PeakSWC.RemoteBlazorWebView.Wpf
{

    public class RemoteBlazorWebView : BlazorWebViewBaseWpf, IBlazorWebView
    {
        #region Properties

        public static readonly DependencyProperty UriProperty = DependencyProperty.Register(
            name: nameof(ServerUri),
            propertyType: typeof(Uri),
            ownerType: typeof(RemoteBlazorWebView),
            typeMetadata: new PropertyMetadata(OnServerUriPropertyChanged));

        public static readonly DependencyProperty IdProperty = DependencyProperty.Register(
                   name: nameof(Id),
                   propertyType: typeof(Guid),
                   ownerType: typeof(RemoteBlazorWebView),
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
            DependencyProperty.Register(nameof(IsRestarting), typeof(bool), typeof(RemoteBlazorWebView), new PropertyMetadata(false));

        private static void OnServerUriPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((RemoteBlazorWebView)d).OnServerUriPropertyChanged(e);

        private void OnServerUriPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private static void OnIdPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((RemoteBlazorWebView)d).OnIdPropertyChanged(e);

        private void OnIdPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        public IWebViewManager? WebViewManager { get; set; }

        public override IWebViewManager CreateWebViewManager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath)
        {
            WebViewManager = new RemoteWebView2Manager(webview, services, dispatcher, fileProvider, hostPageRelativePath, ServerUri, Id);
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
