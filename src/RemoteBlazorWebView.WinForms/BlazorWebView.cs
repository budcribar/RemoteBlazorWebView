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

namespace PeakSWC.RemoteBlazorWebView.WindowsForms
{
    public partial class BlazorWebView : BlazorWebViewFormBase, IBlazorWebView
    {
        private bool IsRefreshing { get; set; } = false;

        public override IFileProvider CreateFileProvider(string contentRootDir)
        {
            IFileProvider provider= null;
            var root = Path.GetDirectoryName(HostPage);
            try
            {
                EmbeddedFilesManifest manifest = ManifestParser.Parse(Assembly.GetEntryAssembly());
                var dir = manifest._rootDirectory.Children.Where(x => x is ManifestDirectory && (x as ManifestDirectory).Children.Any(y => y.Name == root)).FirstOrDefault();

                if (dir != null)
                {
                    var manifestRoot = Path.Combine(dir.Name, root);
                    provider = new ManifestEmbeddedFileProvider(Assembly.GetEntryAssembly(), manifestRoot);
                }
            }
            catch (Exception)
            {
                try
                {
                    EmbeddedFilesManifest manifest = ManifestParser.Parse(new FixedManifestEmbeddedAssembly(Assembly.GetEntryAssembly()));
                    var dir = manifest._rootDirectory.Children.Where(x => x is ManifestDirectory && (x as ManifestDirectory).Children.Any(y => y.Name == root)).FirstOrDefault();

                    if (dir != null)
                    {
                        var manifestRoot = Path.Combine(dir.Name, root);
                        provider = new ManifestEmbeddedFileProvider(new FixedManifestEmbeddedAssembly(Assembly.GetEntryAssembly()), manifestRoot);
                    }
                    
                }
                catch (Exception) {  }
            }
            return provider;
        }
        
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
            if (!IsRefreshing)
                Disconnected?.Invoke(this, args);
        }

        public void FireRefreshed(RefreshedEventArgs args)
        {
            IsRefreshing = true;
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
            RemoteWebView.WebView2WebViewManager m;
            if (ServerUri == null)
                 m = new RemoteWebView.WebView2WebViewManager(webview, services, dispatcher, fileProvider,store, hostPageRelativePath);
            else
                 m  = new RemoteWebView2Manager(this, webview, services, dispatcher, fileProvider, store,hostPageRelativePath, ServerUri, Id.ToString(), Group, Markup);

            return m;
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
