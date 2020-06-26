using BlazorWebView;
using Microsoft.JSInterop;
using PeakSwc.RemoteableWebWindows;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;


namespace RemoteBlazorWebView.Wpf
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class RemoteBlazorWebView : UserControl, IBlazorWebView
    {
        private IBlazorWebView innerBlazorWebView;
        private Grid grid;
        private ViewModel model = new ViewModel();

        static RemoteBlazorWebView()
        {
            //DefaultStyleKeyProperty.OverrideMetadata(typeof(RemoteBlazorWebView), new FrameworkPropertyMetadata(typeof(RemoteBlazorWebView)));
        }

        public RemoteBlazorWebView()
        {
            InitializeComponent();
            DataContext = model;
        }

        public IDisposable Run<TStartup>(string hostHtmlPath, ResolveWebResourceDelegate defaultResolveDelegate = null, Uri uri = null)
        {
            
            if (uri == null)
            {
                innerBlazorWebView = MainBlazorWebView;
                model.ShowHyperlink = "Hidden";
                //grid = new Grid();

                //grid.Children.Add((BlazorWebView.Wpf.BlazorWebView)this.innerBlazorWebView);
                //Content = grid;
            }
            else
            {
                model.Uri = uri.ToString();
                model.ShowHyperlink = "Visible";

                innerBlazorWebView = new RemotableWebWindow(uri, hostHtmlPath);

            }


            IDisposable disposable = BlazorWebViewHost.Run<TStartup>(innerBlazorWebView, "wwwroot/index.html");


            if (innerBlazorWebView is RemotableWebWindow rww)
                rww.JSRuntime = typeof(BlazorWebViewHost).GetProperties(BindingFlags.Static | BindingFlags.NonPublic).Where(x => x.Name == "JSRuntime").FirstOrDefault()?.GetGetMethod(true)?.Invoke(null, null) as JSRuntime;

            return disposable;

        }


        public event EventHandler<string> OnWebMessageReceived
        {
            add
            {
                this.innerBlazorWebView.OnWebMessageReceived += value;
            }

            remove
            {
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
            innerBlazorWebView.Initialize(configure);
        }

        public void Invoke(Action callback)
        {
            innerBlazorWebView.Invoke(callback);
        }

        public void NavigateToUrl(string url)
        {
            innerBlazorWebView.NavigateToUrl(url);
        }

        public void SendMessage(string message)
        {
            innerBlazorWebView.SendMessage(message);
        }

        public void ShowMessage(string title, string message)
        {
            innerBlazorWebView.ShowMessage(title, message);
        }

        

        private void Hyperlink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("explorer", model.Uri);
        }

    }
}
