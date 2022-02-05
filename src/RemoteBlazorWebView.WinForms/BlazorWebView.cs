using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSWC.RemoteWebView;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using WebView2Control = Microsoft.Web.WebView2.WinForms.WebView2;

namespace PeakSWC.RemoteBlazorWebView.WindowsForms
{
    public partial class BlazorWebView : BlazorWebViewFormBase, IBlazorWebView
    {
        private bool IsRefreshing { get; set; } = false;

        public override IFileProvider CreateFileProvider(string contentRootDir) => RemoteWebView.RemoteWebView.CreateFileProvider(contentRootDir, HostPage);

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

        private void ResetServerUri() => ServerUri = new Uri("https://localhost:5001");

        private bool ShouldSerializeServerUri() => ServerUri != null;

        private string _group = "test";

        public void FireConnected(ConnectedEventArgs args)
		{
            Invoke(() => Connected?.Invoke(this, args));
		}

        public void FireDisconnected(DisconnectedEventArgs args)
        {
            if (!IsRefreshing)
                Invoke(() => Disconnected?.Invoke(this, args));
        }

        public void FireRefreshed(RefreshedEventArgs args)
        {
            IsRefreshing = true;
            Invoke(() => Refreshed?.Invoke(this, args));
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
                //StartWebViewCoreIfPossible();
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
                //StartWebViewCoreIfPossible();
            }
        }
        private string _markup = "";
        private void ResetMarkup() => _markup = "";
        private bool ShouldSerializeMarkup() => _markup != "";

        public override WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath)
        {
            if (ServerUri == null)
                 return new WebView2WebViewManager(webview, services, dispatcher, fileProvider,store, hostPageRelativePath);
            else
                 return new RemoteWebView2Manager(this, webview, services, dispatcher, fileProvider, store,hostPageRelativePath);
        }

        public void Restart() => RemoteWebView.RemoteWebView.Restart(this);

        public void NavigateToString(string htmlContent) => WebViewManager.NavigateToString(htmlContent);

        private string remoteHomePage = "";
        public string RemoteHomePage
        {
            get
            {
                if (remoteHomePage.Length == 0)
                    remoteHomePage = $"<a href='{ServerUri}app/{Id}' target='_blank'> {ServerUri}app/{Id}</a>";
                return remoteHomePage;
            }
            set => remoteHomePage = value;
        }
    }
}
