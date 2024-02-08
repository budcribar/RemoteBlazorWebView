using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSWC.RemoteWebView;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using WebView2Control = Microsoft.Web.WebView2.Wpf.WebView2;
using Microsoft.AspNetCore.Components.WebView;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Extensions.Logging;

namespace PeakSWC.RemoteBlazorWebView.Wpf
{

    public class BlazorWebView : BlazorWebViewBase, IBlazorWebView
    {
        public CoreWebView2CookieManager CookieManager  => WebView.CoreWebView2.CookieManager;
        private bool IsRefreshing { get; set; } = false;

        #region Properties

        public static readonly DependencyProperty UriProperty = DependencyProperty.Register(
            name: nameof(ServerUri),
            propertyType: typeof(Uri),
            ownerType: typeof(BlazorWebView),
            typeMetadata: new PropertyMetadata(OnServerUriPropertyChanged));

        public static readonly DependencyProperty GrpcBaseUriProperty = DependencyProperty.Register(
          name: nameof(GrpcBaseUri),
          propertyType: typeof(Uri),
          ownerType: typeof(BlazorWebView),
          typeMetadata: new PropertyMetadata(OnGrpcBaseUriPropertyChanged));

        public static readonly DependencyProperty GroupProperty = DependencyProperty.Register(
                   name: nameof(Group),
                   propertyType: typeof(string),
                   ownerType: typeof(BlazorWebView),
                   typeMetadata: new PropertyMetadata(OnGroupPropertyChanged));

        public static readonly DependencyProperty MarkupProperty = DependencyProperty.Register(
                  name: nameof(Markup),
                  propertyType: typeof(string),
                  ownerType: typeof(BlazorWebView),
                  typeMetadata: new PropertyMetadata(OnMarkupPropertyChanged));

        public static readonly DependencyProperty EnableMirrorsProperty = DependencyProperty.Register(
                 name: nameof(EnableMirrors),
                 propertyType: typeof(bool),
                 ownerType: typeof(BlazorWebView),
                 typeMetadata: new PropertyMetadata(OnEnableMirrorsPropertyChanged));
        #endregion

        public Uri? ServerUri
        {
            get => (Uri?)GetValue(UriProperty);
            set => SetValue(UriProperty, value);
        }

        public Uri? GrpcBaseUri
        {
            get => (Uri?)GetValue(GrpcBaseUriProperty);
            set => SetValue(GrpcBaseUriProperty, value);
        }


        public string Group
        {
            get => (string)GetValue(GroupProperty);
            set => SetValue(GroupProperty, value);
        }

        public string Markup
        {
            get => (string)GetValue(MarkupProperty);
            set => SetValue(MarkupProperty, value);
        }

        public bool EnableMirrors
        {
            get => (bool)GetValue(EnableMirrorsProperty);
            set => SetValue(EnableMirrorsProperty, value);
        } 

        private static void OnServerUriPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnServerUriPropertyChanged(e);

        private void OnServerUriPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private static void OnGrpcBaseUriPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnGrpcBaseUriPropertyChanged(e);

        private void OnGrpcBaseUriPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private static void OnIdPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnIdPropertyChanged(e);

        private void OnIdPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private static void OnGroupPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnGroupPropertyChanged(e);

        private void OnGroupPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private static void OnMarkupPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnMarkupPropertyChanged(e);

        private void OnMarkupPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private static void OnEnableMirrorsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnEnableMirrorsPropertyChanged(e);

        private void OnEnableMirrorsPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private Guid id = Guid.Empty;
        public Guid Id
        {
            get
            {
                if (id == Guid.Empty)
                    throw new Exception("Id not initialized");

                return id;
            }
            set
            {
                if (value == Guid.Empty)
                    id = Guid.NewGuid();
                else
                    id = value;
            }
        }

        public override IFileProvider CreateFileProvider(string contentRootDir) => RemoteWebView.RemoteWebView.CreateFileProvider(contentRootDir,HostPage);

        public override WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath,string hostPagePathWithinFileProvider, Action<UrlLoadingEventArgs> externalNavigationStarting,Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing, Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized,ILogger logger)
        {
            if (ServerUri == null)
                return new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath, hostPagePathWithinFileProvider, externalNavigationStarting, blazorWebViewInitializing, blazorWebViewInitialized,logger);
            else
                return new RemoteWebView2Manager(this,webview, services, dispatcher, fileProvider,store, hostPageRelativePath, hostPagePathWithinFileProvider,externalNavigationStarting, blazorWebViewInitializing, blazorWebViewInitialized, logger);
        }

        public void FireConnected(ConnectedEventArgs args)
        {
            Dispatcher.Invoke(() => Connected?.Invoke(this, args));
        }

        public void FireDisconnected(DisconnectedEventArgs args)
        {
            if (!IsRefreshing)
                Dispatcher.Invoke(() => Disconnected?.Invoke(this, args));
        }

        public void FireRefreshed(RefreshedEventArgs args)
        {
            IsRefreshing = true;
            Dispatcher.Invoke(() => Refreshed?.Invoke(this, args));
        }

        public void FireReadyToConnect(ReadyToConnectEventArgs args)
        {
            Dispatcher.Invoke(() => ReadyToConnect?.Invoke(this, args));
        }

        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;
        public event EventHandler<ReadyToConnectEventArgs>? ReadyToConnect;

        private void HandleRootComponentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            // TODO wtf?
            Services = this.Services;
            RootComponents.ToList().ForEach(x => RootComponents.Add(x));
            HostPage = HostPage;
            Group = Group;
            Markup = Markup;
            ServerUri = ServerUri;

            // TODO
            if (ServerUri != null)
               Id = Id;
        }

        public void Restart() => RemoteWebView.RemoteWebView.Restart(this);

        public Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri) => RemoteWebView.RemoteWebView.GetGrpcBaseUriAsync(serverUri);

        public void NavigateToString(string htmlContent) => WebViewManager.NavigateToString(htmlContent);


        public Task WaitForInitializationComplete() => Task.CompletedTask;
    }
}
