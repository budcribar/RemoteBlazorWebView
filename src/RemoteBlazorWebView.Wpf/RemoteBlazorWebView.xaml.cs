using Microsoft.AspNetCore.StaticFiles;
using Microsoft.JSInterop;
using PeakSwc.RemoteableWebWindows;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.AspNetCore.Components.WebView.Wpf;
using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using System.Collections.Specialized;

namespace RemoteBlazorWebView.Wpf
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class RemoteBlazorWebView : UserControl
    {
        private WebView2WebViewManager manager;


        #region Dependency property definitions
        /// <summary>
        /// The backing store for the <see cref="HostPage"/> property.
        /// </summary>
        public static readonly DependencyProperty HostPageProperty = DependencyProperty.Register(
            name: nameof(HostPage),
            propertyType: typeof(string),
            ownerType: typeof(RemoteBlazorWebView),
            typeMetadata: new PropertyMetadata(OnHostPagePropertyChanged));

        /// <summary>
        /// The backing store for the <see cref="RootComponent"/> property.
        /// </summary>
        public static readonly DependencyProperty RootComponentsProperty = DependencyProperty.Register(
            name: nameof(RootComponents),
            propertyType: typeof(ObservableCollection<RootComponent>),
            ownerType: typeof(RemoteBlazorWebView));

        /// <summary>
        /// The backing store for the <see cref="Services"/> property.
        /// </summary>
        public static readonly DependencyProperty ServicesProperty = DependencyProperty.Register(
            name: nameof(Services),
            propertyType: typeof(IServiceProvider),
            ownerType: typeof(RemoteBlazorWebView),
            typeMetadata: new PropertyMetadata(OnServicesPropertyChanged));
        #endregion

        /// <summary>
        /// Path to the host page within the application's static files. For example, <code>wwwroot\index.html</code>.
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>
        public string HostPage
        {
            get => (string)GetValue(HostPageProperty);
            set => SetValue(HostPageProperty, value);
        }

        /// <summary>
        /// A collection of <see cref="RootComponent"/> instances that specify the Blazor <see cref="IComponent"/> types
        /// to be used directly in the specified <see cref="HostPage"/>.
        /// </summary>
        public ObservableCollection<RootComponent> RootComponents =>
            (ObservableCollection<RootComponent>)GetValue(RootComponentsProperty);

        /// <summary>
        /// Gets or sets an <see cref="IServiceProvider"/> containing services to be used by this control and also by application code.
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>
        public IServiceProvider Services
        {
            get => (IServiceProvider)GetValue(ServicesProperty);
            set => SetValue(ServicesProperty, value);
        }

        private static void OnServicesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((RemoteBlazorWebView)d).OnServicesPropertyChanged(e);

        private void OnServicesPropertyChanged(DependencyPropertyChangedEventArgs e) => StartWebViewCoreIfPossible();

        private void StartWebViewCoreIfPossible()
        {
            //TODO
        }

        private static void OnHostPagePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((RemoteBlazorWebView)d).OnHostPagePropertyChanged(e);

        private void OnHostPagePropertyChanged(DependencyPropertyChangedEventArgs e) => StartWebViewCoreIfPossible();


        private Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView? innerBlazorWebView;
        private RemotableWebWindow? RemotableWebWindow { get; set; } = null;
        private readonly ViewModel model = new ViewModel();
        static RemoteBlazorWebView() { }

        public RemoteBlazorWebView()
        {
            SetValue(RootComponentsProperty, new ObservableCollection<RootComponent>());
            RootComponents.CollectionChanged += HandleRootComponentsCollectionChanged;
            // TODO

            model.ShowHyperlink = "Hidden";
            InitializeComponent();

           
            
           

            
            DataContext = model;
        }
        private void HandleRootComponentsCollectionChanged(object sender, NotifyCollectionChangedEventArgs eventArgs)
        {
            MainBlazorWebView.Services = this.Services;
            RootComponents.ToList().ForEach(x => MainBlazorWebView.RootComponents.Add(x));
            MainBlazorWebView.HostPage = HostPage;
            
        }



        //public IDisposable Run<TStartup>(string hostHtmlPath, ResolveWebResourceDelegate? defaultResolveDelegate = null, Uri? uri = null, Guid id = default)
        //{
        //    if (uri == null)
        //    {
        //        innerBlazorWebView = MainBlazorWebView;
        //        model.ShowHyperlink = "Hidden";
        //    }
        //    else
        //    {
        //        innerBlazorWebView = new RemotableWebWindow(uri, hostHtmlPath, id);
        //    }

        //    IDisposable disposable = BlazorWebViewHost.Run<TStartup>(innerBlazorWebView, hostHtmlPath, defaultResolveDelegate);

        //    if (innerBlazorWebView is RemotableWebWindow rww)
        //    {
        //        //if (FrameworkFileResolver != null)
        //       //     rww.FrameworkFileResolver = FrameworkFileResolver;
        //        rww.JSRuntime = typeof(BlazorWebViewHost).GetProperties(BindingFlags.Static | BindingFlags.NonPublic).Where(x => x.Name == "JSRuntime").FirstOrDefault()?.GetGetMethod(true)?.Invoke(null, null) as JSRuntime;
        //        model.Uri = uri?.ToString() + "app?guid=" + rww.Id;
        //        model.ShowHyperlink = "Visible";
        //    }

        //    return disposable;
        //}

        public event EventHandler<string> OnWebMessageReceived
        {
            add
            {
              
                //if (this.innerBlazorWebView != null)
                //    this.innerBlazorWebView.OnWebMessageReceived += value;
            }

            remove
            {
                //if (this.innerBlazorWebView != null)
                //    this.innerBlazorWebView.OnWebMessageReceived -= value;
            }
        }

        //public event EventHandler<string> OnConnected
        //{
        //    add
        //    {
        //        if (this.innerBlazorWebView is RemotableWebWindow rmm)
        //            rmm.OnConnected += value;
        //    }

        //    remove
        //    {
        //        if (this.innerBlazorWebView is RemotableWebWindow rmm)
        //            rmm.OnConnected -= value;
        //    }
        //}
        public event EventHandler<string> OnDisconnected
        {
            add
            {
                if (RemotableWebWindow != null)
                    RemotableWebWindow.OnDisconnected += value;
            }

            remove
            {
                if (RemotableWebWindow != null)
                    RemotableWebWindow.OnDisconnected -= value;
            }
        }


        //public void Initialize(Action<WebViewOptions> configure)
        //{
        //    innerBlazorWebView?.Initialize(configure);
        //}

        public void Invoke(Action callback)
        {
            // TODO
            //innerBlazorWebView?.Invoke(callback);
        }

        public void NavigateToUrl(string url)
        {
            // TODO
            //innerBlazorWebView?.NavigateToUrl(url);
        }

        public void SendMessage(string message)
        {
           // TODO
           //innerBlazorWebView?.SendMessage(message);
        }

        public void ShowMessage(string title, string message)
        {
            // TODO
            //innerBlazorWebView?.ShowMessage(title, message);
        }      

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            var url = "\"" + model.Uri + "\"";

            try
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start microsoft-edge:" +url) { CreateNoWindow = true });
                model.ShowHyperlink = "Hidden";
            }
            catch (Exception) {
     
            }          
        }

        
        private void MainGrid_Initialized(object sender, EventArgs e)
        {
            RootComponents.ToList().ForEach(x => MainBlazorWebView.RootComponents.Add(x));
        }
    }
}
