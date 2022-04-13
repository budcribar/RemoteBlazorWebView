using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EditWebView
{
    public class Editor
    {
        string text;
        readonly string fileName;
        public Editor(string file)
        {
            text = File.ReadAllText(file);

            var fileName = Path.GetFileName(file);
          
            if (fileName == "BlazorWebView.cs")
                if (file.Contains("WindowsForms"))
                    fileName = "BlazorWebViewFormBase.cs";
                else
                    fileName = "BlazorWebViewBase.cs";

            this.fileName = fileName;
        }

        public void Edit()
        {

            // Two additions for .13 release
            if (fileName == "BlazorWebViewBase.cs")
                Replace("public WebView2Control WebView => _webview;", "public WebView2Control WebView => _webview;\n        public WebView2WebViewManager WebViewManager => _webviewManager;");

            if(fileName == "WebView2WebViewManager.cs")
            {
                string method =
      @"public void NavigateToString(string htmlContent)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await _webviewReadyTask;
                _webview.NavigateToString(htmlContent);
            });
        }";
                Replace("protected override void SendMessage(string message)", method + "\n        protected override void SendMessage(string message)");
                Replace("using Microsoft.AspNetCore.Components.WebView.WindowsForms;", "using PeakSWC.RemoteBlazorWebView.WindowsForms;");
                Replace("using Microsoft.AspNetCore.Components.WebView.Wpf;", "using PeakSWC.RemoteBlazorWebView.Wpf;");

            }

            if (fileName =="BlazorWebViewDeveloperTools.cs")
            {
                Replace("internal class", "public class");
            }
            if (fileName == "WpfBlazorMarkerService.cs")
            {
                Replace("internal class", "public class");
            }

            if (fileName == "BlazorWebViewServiceCollectionExtensions.cs")
            {
                Replace("using Microsoft.AspNetCore.Components.WebView.WindowsForms;", "using PeakSWC.RemoteBlazorWebView.WindowsForms;");
                Replace("using Microsoft.AspNetCore.Components.WebView.Wpf;", "using PeakSWC.RemoteBlazorWebView.Wpf;");
            }

            if (fileName == "UrlLoadingEventArgs.cs")
            {
                Replace("internal static", "public static");
            }

            if (fileName == "BlazorWebViewFormBase.cs" || fileName == "BlazorWebViewBase.cs")
            {
                Comment("#pragma warning disable CA1816");
                Comment("#pragma warning restore");


                //Replace("var fileProvider = CreateFileProvider(contentRootDirFullPath);", "var customFileProvider = CreateFileProvider(contentRootDirFullPath);\n            IFileProvider fileProvider = customFileProvider == null ? new PhysicalFileProvider(contentRootDirFullPath) 	: customFileProvider;");              
                Replace("new WebView2WebViewManager", "CreateWebViewManager");
                Replace("private void StartWebViewCoreIfPossible()", "public virtual WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath,Action<UrlLoadingEventArgs> externalNavigationStarting)\n\t\t{\n\t\t\treturn new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath,externalNavigationStarting);\n\t\t}\n\t\tprotected void StartWebViewCoreIfPossible()");             
                Replace("BlazorWebView", Path.GetFileNameWithoutExtension(fileName));
                InsertUsing("Microsoft.AspNetCore.Components.Web");
                InsertUsing("Microsoft.AspNetCore.Components.WebView");
                //Replace("WebView2WebViewManager", "WebView2.WebView2WebViewManager");
            }

            Replace("namespace Microsoft.AspNetCore.Components.WebView.WindowsForms", "namespace PeakSWC.RemoteBlazorWebView.WindowsForms");
            Replace("namespace Microsoft.AspNetCore.Components.WebView.Wpf", "namespace PeakSWC.RemoteBlazorWebView.Wpf");
            Replace("namespace Microsoft.AspNetCore.Components.WebView.WebView2", "namespace PeakSWC.RemoteBlazorWebView");

            Replace("namespace Microsoft.AspNetCore.Components.WebView", "namespace PeakSWC.RemoteBlazorWebView");
            Replace("using Microsoft.Web.WebView2;", "using Microsoft.Web.WebView2;\nusing Microsoft.AspNetCore.Components.WebView;\nusing Microsoft.AspNetCore.Components;");
           
            if (fileName == "RootComponent.cs" || fileName == "WpfDispatcher.cs" || fileName == "BlazorWebViewBase.cs" || fileName == "WindowsFormsDispatcher.cs" || fileName == "BlazorWebViewFormBase.cs" || fileName == "RootComponentCollectionExtensions.cs")
                InsertUsing("Microsoft.AspNetCore.Components");

            if (fileName == "RootComponent.cs")
            {
                //Replace("WebView2WebViewManager", "WebView2.WebView2WebViewManager");
                InsertUsing("Microsoft.AspNetCore.Components.WebView");
            }

            if (fileName == "RootComponent.cs" || fileName == "BlazorWebViewFormBase.cs" || fileName == "BlazorWebViewBase.cs")
                Replace("using Microsoft.AspNetCore.Components.WebView.WebView2;", "using WebView2 = Microsoft.AspNetCore.Components.WebView.WebView2;");
        }

        public void WriteAllText(string outputDir)
        {
            File.WriteAllText(Path.Combine(outputDir, fileName), text);
        }

        public void Replace(string oldValue, string newValue)
        {
            text = text.Replace($"{oldValue}", $"{newValue}");
        }

        public void Comment(string target)
        {
            text = text.Replace($"{target}", $"//{target}");
        }
        public void InsertUsing (string nameSpace) {
            text = text.Replace("using System;", $"using System;\nusing {nameSpace};");
        }
    }
}
