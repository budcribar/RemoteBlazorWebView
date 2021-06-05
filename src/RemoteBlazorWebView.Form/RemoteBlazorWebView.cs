
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView.WebView2;
using Microsoft.Extensions.FileProviders;
using PeakSwc.RemoteableWebWindows;
using PeakSWC;
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace Remote.WebView.WindowsForms
{
    public partial class RemoteBlazorWebView : BlazorWebViewFormBase
    {
        private Uri _serverUri;

        /// <summary>
        /// Uri of the RemoteableWebView service.
        /// This property must be set to a valid value for the Blazor components to start.
        /// </summary>

        [TypeConverter(typeof(UriTypeConverter))]
        [Category("Behavior")]
        [Description(@"Uri of the RemoteableWebView service.")]
        public Uri ServerUri
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
                _id = value;
                Invalidate();
                StartWebViewCoreIfPossible();
            }
        }
        private void ResetId() => Id = Guid.Empty;
        private bool ShouldSerializeId() => Id != Guid.Empty;



        public override IWebViewManager CreateWebViewManager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath)
        {
            return new RemoteWebView2Manager(webview, services, dispatcher, fileProvider, hostPageRelativePath, ServerUri, Id);
        }

    }
}
