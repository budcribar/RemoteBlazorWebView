using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSWC.RemoteableWebView;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace PeakSWC.RemoteBlazorWebView.WebView.WindowsForms
{
    public partial class RemoteBlazorWebView : BlazorWebViewFormBase, IBlazorWebView
    {


        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            foreach (var h in LoadedInternal.ToArray())
            {
                Loaded += h;
                LoadedInternal.Remove(h);
            }
            foreach (var h in UnloadedInternal.ToArray())
            {
                Unloaded += h;
                UnloadedInternal.Remove(h);
            }
        }

        public IWebViewManager? WebViewManager { get; set; }

        private Uri? _serverUri;

        /// <summary>
        /// Uri of the RemoteableWebView service.
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>

        [TypeConverter(typeof(UriTypeConverter))]
        [Category("Behavior")]
        [Description(@"Uri of the RemoteableWebView service.")]
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
        private void ResetServerUri() => ServerUri = new Uri("https://localhost:443");

        private bool ShouldSerializeServerUri() => ServerUri != null;

        private Guid _id;


        private readonly List<EventHandler<string>> UnloadedInternal = new();
        private readonly List<EventHandler<string>> LoadedInternal = new();
        public event EventHandler<string> Unloaded
        {
            add
            {
                // TODO
                if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteableWebView != null)
                    manager.RemoteableWebView.OnDisconnected += value;
                else
                    UnloadedInternal.Add(value);
            }

            remove
            {
                if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteableWebView != null)
                    manager.RemoteableWebView.OnDisconnected -= value;
            }
        }

        public event EventHandler<string> Loaded
        {
            add
            {
                // TODO
                if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteableWebView != null)
                    manager.RemoteableWebView.OnConnected += value;
                else
                    LoadedInternal.Add(value);

            }

            remove
            {
                if (WebViewManager is RemoteWebView2Manager manager && manager.RemoteableWebView != null)
                    manager.RemoteableWebView.OnConnected -= value;
            }
        }


        /// <summary>
        /// Optional Id associated with the client
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>
        [TypeConverter(typeof(GuidConverter))]
        [Category("Behavior")]
        [Description(@"Optional Id associated with the client.")]
        public Guid Id
        {
            get => _id;
            set
            {
                if (value == Guid.Empty)
                    value = Guid.NewGuid();
                _id = value;
                Invalidate();
                StartWebViewCoreIfPossible();
            }
        }

        public bool IsRestarting { get; set; }

        private void ResetId() => Id = Guid.Empty;
        private bool ShouldSerializeId() => Id != Guid.Empty;

        public override IWebViewManager CreateWebViewManager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath)
        {
            WebViewManager = new RemoteWebView2Manager(webview, services, dispatcher, fileProvider, hostPageRelativePath, ServerUri, Id);

            return WebViewManager;
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
