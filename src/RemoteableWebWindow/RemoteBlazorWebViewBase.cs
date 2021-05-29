using RemoteBlazorWebView.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;

namespace PeakSWC
{
    public class RemoteBlazorWebViewBase : BlazorWebViewBase
    {
        public Uri? ServerUri { get; set; }
        public Guid Id { get; set; }
       
        protected override void StartWebViewCoreIfPossible() {
            CheckDisposed();

            if (!RequiredStartupPropertiesSet || _webviewManager != null)
            {
                return;
            }

            // We assume the host page is always in the root of the content directory, because it's
            // unclear there's any other use case. We can add more options later if so.
            var contentRootDir = Path.GetDirectoryName(Path.GetFullPath(HostPage));
            if (contentRootDir == null) throw new Exception("No root directory found");
            var hostPageRelativePath = Path.GetRelativePath(contentRootDir, HostPage);
            var fileProvider = new PhysicalFileProvider(contentRootDir);

            _webviewManager = new PeakSWC.RemoteWebView2Manager(new WpfWeb2ViewWrapper(_webview), Services, WpfDispatcher.Instance, fileProvider, hostPageRelativePath, ServerUri, Id);
            foreach (var rootComponent in RootComponents)
            {
                // Since the page isn't loaded yet, this will always complete synchronously
                _ = rootComponent.AddToWebViewManagerAsync(_webviewManager);
            }
            _webviewManager.Navigate("/");
        }
    }
}
