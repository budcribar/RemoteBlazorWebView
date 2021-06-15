using PeakSwc.RemoteableWebWindows;
using System;
using System.Linq;
using System.Windows;
using System.Collections.Specialized;
using PeakSWC;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.Components;

namespace RemoteBlazorWebView.Wpf
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

        /// <summary>
        /// Path to the host page within the application's static files. For example, <code>wwwroot\index.html</code>.
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>
        /// 
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

        private readonly ViewModel model = new();
        static RemoteBlazorWebView() { }

        public RemoteBlazorWebView()
        {
            //SetValue(RootComponentsProperty, new ObservableCollection<RootComponent>());
            //RootComponents.CollectionChanged += HandleRootComponentsCollectionChanged;
            // TODO

            model.ShowHyperlink = "Hidden";
         
            DataContext = model;
        }


        private void HandleRootComponentsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            Services = this.Services;
            RootComponents.ToList().ForEach(x => RootComponents.Add(x));
            HostPage = HostPage;
           
            if (ServerUri == null)
            {
                //innerBlazorWebView = MainBlazorWebView;
                model.ShowHyperlink = "Hidden";
            }
            else 
            {
                ServerUri = ServerUri;
                if (Id == default) Id = Guid.NewGuid();
                Id = Id;
                model.Uri = $"{ServerUri}app/{Id}";
                model.ShowHyperlink = IsRestarting ? "Hidden" : "Visible";
            }
        }

        //public event EventHandler<string> OnWebMessageReceived
        //{
        //    add
        //    {
              
        //        //if (this.innerBlazorWebView != null)
        //        //    this.innerBlazorWebView.OnWebMessageReceived += value;
        //    }

        //    remove
        //    {
        //        //if (this.innerBlazorWebView != null)
        //        //    this.innerBlazorWebView.OnWebMessageReceived -= value;
        //    }
        //}

        //public void Invoke(Action callback)
        //{
        //    // TODO
        //    //innerBlazorWebView?.Invoke(callback);
        //}

        public void NavigateToUrl(string _)
        {
            // TODO
            //innerBlazorWebView?.NavigateToUrl(url);
        }

        //public void SendMessage(string message)
        //{
        //   // TODO
        //   //innerBlazorWebView?.SendMessage(message);
        //}

        //public void ShowMessage(string title, string message)
        //{
        //    // TODO
        //    //innerBlazorWebView?.ShowMessage(title, message);
        //}      

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            model.ShowHyperlink = "Hidden";
            RemotableWebWindow.StartBrowser(this);  
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
