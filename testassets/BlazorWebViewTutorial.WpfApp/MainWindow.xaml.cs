using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using PeakSwc.RemoteableWebWindows;



namespace BlazorWebViewTutorial.WpfApp
{
    // add usings here
    using BlazorWebView.Wpf;
    using BlazorWebView;
    using System.Threading;
    using Microsoft.JSInterop;
    using System.Reflection;
    using System.Diagnostics;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IDisposable disposable;
        private bool initialized = false;

        public MainWindow()
        {
            InitializeComponent();
        }

        public Uri Uri { get; set; } = null;

        private void Window_ContentRendered(object sender, EventArgs e)
        {
            if (!this.initialized)
            {
                this.initialized = true;
                this.RemoteBlazorWebView.Run<Startup>("wwwroot/index.html", null, Uri);
        
                this.RemoteBlazorWebView.OnDisconnected += (s, e) =>  Application.Current.Dispatcher.Invoke(Close);
                // this.RemoteBlazorWebView.OnConnected += (s, e) => { this.RemoteBlazorWebView.ShowMessage("Title", "Hello World"); };
            }
        }


        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (this.disposable != null)
            {
                this.disposable.Dispose();
                this.disposable = null;
            }
        }

       
    }
}
