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
namespace PeakSWC.RemoteBlazorWebView.Wpf
{

    public class BlazorWebView : BlazorWebViewBase, IBlazorWebView
    {
        private bool IsRefreshing { get; set; } = false;

        #region Properties

        public static readonly DependencyProperty UriProperty = DependencyProperty.Register(
            name: nameof(ServerUri),
            propertyType: typeof(Uri),
            ownerType: typeof(BlazorWebView),
            typeMetadata: new PropertyMetadata(OnServerUriPropertyChanged));

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
        #endregion

        public Uri? ServerUri
        {
            get => (Uri?)GetValue(UriProperty);
            set => SetValue(UriProperty, value);
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

        private static void OnGroupPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnGroupPropertyChanged(e);

        private void OnGroupPropertyChanged(DependencyPropertyChangedEventArgs _) { }

        private static void OnMarkupPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BlazorWebView)d).OnMarkupPropertyChanged(e);

        private void OnMarkupPropertyChanged(DependencyPropertyChangedEventArgs _) { }

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

        public override IFileProvider CreateFileProvider(string contentRootDir)
        {
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
            return provider;
        }

        public override WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath)
        {
            if (ServerUri == null)
                return new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath);
            else
                return new RemoteWebView2Manager(this,webview, services, dispatcher, fileProvider,store, hostPageRelativePath);
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

        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;

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

        public void Restart()
        {
            RemoteWebView.RemoteWebView.Restart(this);
        }

        public Task<Process?> StartBrowser()
        {
            return RemoteWebView.RemoteWebView.StartBrowser(this);
        }
    }
}
