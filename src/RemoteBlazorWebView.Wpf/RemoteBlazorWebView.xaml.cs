using BlazorWebView;
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


namespace RemoteBlazorWebView.Wpf
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class RemoteBlazorWebView : UserControl, IBlazorWebView
    {
        private IBlazorWebView? innerBlazorWebView;
        private readonly ViewModel model = new ViewModel();
        static RemoteBlazorWebView() { }

        public RemoteBlazorWebView()
        {
            InitializeComponent();
            DataContext = model;
        }

        public Func<string, byte[]>? FrameworkFileResolver { get; set; }

        public IDisposable Run<TStartup>(string hostHtmlPath, ResolveWebResourceDelegate? defaultResolveDelegate = null, Uri? uri = null)
        {
            if (uri == null)
            {
                innerBlazorWebView = MainBlazorWebView;
                model.ShowHyperlink = "Hidden";
            }
            else
            {
                innerBlazorWebView = new RemotableWebWindow(uri, hostHtmlPath);
            }

            IDisposable disposable = BlazorWebViewHost.Run<TStartup>(innerBlazorWebView, hostHtmlPath, defaultResolveDelegate);

            if (innerBlazorWebView is RemotableWebWindow rww)
            {
                if (FrameworkFileResolver != null)
                    rww.FrameworkFileResolver = FrameworkFileResolver;
                rww.JSRuntime = typeof(BlazorWebViewHost).GetProperties(BindingFlags.Static | BindingFlags.NonPublic).Where(x => x.Name == "JSRuntime").FirstOrDefault()?.GetGetMethod(true)?.Invoke(null, null) as JSRuntime;
                model.Uri = uri?.ToString() + "app?guid=" + rww.Id;
                model.ShowHyperlink = "Visible";
            }

            return disposable;
        }

        public event EventHandler<string> OnWebMessageReceived
        {
            add
            {
                if (this.innerBlazorWebView != null)
                    this.innerBlazorWebView.OnWebMessageReceived += value;
            }

            remove
            {
                if (this.innerBlazorWebView != null)
                    this.innerBlazorWebView.OnWebMessageReceived -= value;
            }
        }

        public event EventHandler OnConnected
        {
            add
            {
                if (this.innerBlazorWebView is RemotableWebWindow rmm)
                    rmm.OnConnected += value;
            }

            remove
            {
                if (this.innerBlazorWebView is RemotableWebWindow rmm)
                    rmm.OnConnected -= value;
            }
        }
        public event EventHandler OnDisconnected
        {
            add
            {
                if (this.innerBlazorWebView is RemotableWebWindow rmm)
                    rmm.OnDisconnected += value;
            }

            remove
            {
                if (this.innerBlazorWebView is RemotableWebWindow rmm)
                    rmm.OnDisconnected -= value;
            }
        }


        public void Initialize(Action<WebViewOptions> configure)
        {
            innerBlazorWebView?.Initialize(configure);
        }

        public void Invoke(Action callback)
        {
            innerBlazorWebView?.Invoke(callback);
        }

        public void NavigateToUrl(string url)
        {
            innerBlazorWebView?.NavigateToUrl(url);
        }

        public void SendMessage(string message)
        {
            innerBlazorWebView?.SendMessage(message);
        }

        public void ShowMessage(string title, string message)
        {
            innerBlazorWebView?.ShowMessage(title, message);
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

    }
}
