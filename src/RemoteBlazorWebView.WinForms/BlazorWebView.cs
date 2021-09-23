using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSWC.RemoteWebView;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PeakSWC.RemoteBlazorWebView.WindowsForms
{
    public partial class BlazorWebView : BlazorWebViewFormBase, IBlazorWebView
    {


        protected override void OnCreateControl()
        {
            base.OnCreateControl();
            Application.ApplicationExit += Application_ApplicationExit;
        }

        private void Application_ApplicationExit(object? sender, EventArgs e)
        {
            if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteWebView != null)
                manager.RemoteWebView.Shutdown();
        }

        public RemoteWebView.WebView2WebViewManager? WebViewManager { get; set; }

        private Uri? _serverUri;

        /// <summary>
        /// Uri of the RemoteWebView service.
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>

        [TypeConverter(typeof(UriTypeConverter))]
        [Category("Behavior")]
        [Description(@"Uri of the RemoteWebView service.")]
        public Uri? ServerUri
        {
            get => _serverUri;
            set
            {
                _serverUri = value;
                Invalidate();
                StartWebViewCoreIfPossible();
            }
        }

        private Guid id = Guid.Empty;
        public Guid Id
        {
            get
            {
                if (id == Guid.Empty)
                    id = Guid.NewGuid();

                return id;
            }
            set
            {
                if (value == Guid.Empty)
                    id = Guid.NewGuid();
                id = value;
            }
        }

        private void ResetServerUri() => ServerUri = new Uri("https://localhost:5001");

        private bool ShouldSerializeServerUri() => ServerUri != null;

        private string _group = "test";

        public void FireConnected(ConnectedEventArgs args)
		{
            Connected?.Invoke(this, args);
		}

        public void FireDisconnected(DisconnectedEventArgs args)
        {
            Disconnected?.Invoke(this, args);
        }

        public void FireRefreshed(RefreshedEventArgs args)
        {
            Refreshed?.Invoke(this, args);
        }

        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;

        /// <summary>
        /// Group that the user is a member of when signed in
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>
       
        [Category("Behavior")]
        [Description(@"Group associated with the user.")]
        public string Group
        {
            get => _group;
            set
            {            
                _group = value;
                Invalidate();
                StartWebViewCoreIfPossible();
            }
        }
        private void ResetGroup() => _group = "test";
        private bool ShouldSerializeGroup() => _group != "test";
       
        /// <summary>
        /// Markup that is used to identify the client
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>

        [Category("Behavior")]
        [Description(@"Html markup associated with the client.")]
        public string Markup
        {
            get => _markup;
            set
            {
                _markup = value;
                Invalidate();
                StartWebViewCoreIfPossible();
            }
        }
        private string _markup = "";
        private void ResetMarkup() => _markup = "";
        private bool ShouldSerializeMarkup() => _markup != "";

        public bool IsRestarting { get; set; }

       

        public override RemoteWebView.WebView2WebViewManager CreateWebViewManager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath)
        {
            if (ServerUri == null)
                WebViewManager = new RemoteWebView.WebView2WebViewManager(webview, services, dispatcher, fileProvider,store, hostPageRelativePath);
            else
                WebViewManager = new RemoteWebView2Manager(this, webview, services, dispatcher, fileProvider, store,hostPageRelativePath, ServerUri, Id.ToString(), Group, Markup);

            return WebViewManager;
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
