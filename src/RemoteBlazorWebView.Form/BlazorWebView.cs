using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSWC.RemoteableWebView;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace PeakSWC.RemoteBlazorWebView.WindowsForms
{
    public partial class BlazorWebView : BlazorWebViewFormBase, IBlazorWebView
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

        public Guid Id
        {
            get
            {
                string? id = (WebViewManager as RemoteWebView2Manager)?.RemoteableWebView?.Id;
                return (id == null) ? Guid.Empty : new Guid(id);
            }
            set
            {
                var rww = (WebViewManager as RemoteWebView2Manager)?.RemoteableWebView;
                if (rww != null) rww.Id = value.ToString();
            }
        }

        private void ResetServerUri() => ServerUri = new Uri("https://localhost:443");

        private bool ShouldSerializeServerUri() => ServerUri != null;

        private string _group = "test";


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

        public bool IsRestarting { get; set; }

        private void ResetGroup() => _group = "test";
        private bool ShouldSerializeId() => _group != "test";

        public override IWebViewManager CreateWebViewManager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath)
        {
            if (ServerUri == null)
                WebViewManager = new RemoteableWebView.WebView2WebViewManager(webview, services, dispatcher, fileProvider, hostPageRelativePath);
            else
                WebViewManager = new RemoteWebView2Manager(webview, services, dispatcher, fileProvider, hostPageRelativePath, ServerUri, Group);

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
