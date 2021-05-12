

using Microsoft.AspNetCore.Components.WebView.Wpf;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace RemoteBlazorWebView.Wpf
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public interface IBlazorWebView 
    {
        public string HostPage { get; set; }
        
        public IServiceProvider Services { get; set; }

        public ObservableCollection<RootComponent> RootComponents { get; }

        public event RoutedEventHandler Unloaded;
        public event RoutedEventHandler Loaded;

        //private void MainBlazorWebView_Loaded(object sender, RoutedEventArgs e)
        //private void MainBlazorWebView_Unloaded(object sender, RoutedEventArgs e)


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

       
    }
}
