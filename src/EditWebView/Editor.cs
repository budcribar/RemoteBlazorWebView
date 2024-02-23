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

                Replace("AddWindowsFormsBlazorWebView", "AddRemoteWindowsFormsBlazorWebView");
                Replace("AddWpfBlazorWebView", "AddRemoteWpfBlazorWebView");

                Replace("internal class WebView2WebViewManager", "public class WebView2WebViewManager");
                //ReplaceFirst("using Microsoft.AspNetCore.Components.WebView;", "");

                ReplaceFirst("#elif WEBVIEW2_MAUI", "using Microsoft.AspNetCore.Components.WebView;\nusing Microsoft.AspNetCore.Components;\n#elif WEBVIEW2_MAUI");

            }
            if (fileName == "UrlLoadingEventArgs.cs")
            {
                
                
                Replace("var strategy = appOriginUri.IsBaseOf(urlToLoad) ?", "var split = urlToLoad.AbsolutePath.Split('/');\n			var isMirrorUrl = split.Length == 3 && split[1] == \"mirror\" && Guid.TryParse(split[2], out Guid _);\n			var strategy = (appOriginUri.IsBaseOf(urlToLoad) || urlToLoad.Scheme == \"data\" || isMirrorUrl) ?");
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
                Replace("namespace Microsoft.Extensions.DependencyInjection", "namespace PeakSWC.RemoteBlazorWebView");
                Replace("AddWindowsFormsBlazorWebView", "AddRemoteWindowsFormsBlazorWebView");
                Replace("AddWpfBlazorWebView", "AddRemoteWpfBlazorWebView");
                Replace("AddBlazorWebViewDeveloperTools", "AddRemoteBlazorWebViewDeveloperTools");
                
            }

            if (fileName == "UrlLoadingEventArgs.cs")
            {
                Replace("internal static", "public static");
            }

            if (fileName == "BlazorWebViewFormBase.cs" || fileName == "BlazorWebViewBase.cs")
            {
                Comment("#pragma warning disable CA1816");
                Comment("#pragma warning restore");

                Replace("Dock = DockStyle.Fill,", "Dock = DockStyle.Fill, AllowExternalDrop = false");
                Replace("_webview = (WebView2Control)GetTemplateChild(WebViewTemplateChildName);", "_webview = (WebView2Control)GetTemplateChild(WebViewTemplateChildName);\n\t\t\t\t_webview.AllowExternalDrop = false;");

                // Winforms does not use nullable on WebView2Control
                Replace("public WebView2Control WebView => _webview;", "public WebView2Control WebView => _webview;\n        [Browsable(false)]\n        public WebView2WebViewManager WebViewManager => _webviewManager;");
                Replace("public WebView2Control WebView => _webview!;", "public WebView2Control WebView => _webview!;\n        [Browsable(false)]\n        public WebView2WebViewManager WebViewManager => _webviewManager;");


                //Replace("var fileProvider = CreateFileProvider(contentRootDirFullPath);", "var customFileProvider = CreateFileProvider(contentRootDirFullPath);\n            IFileProvider fileProvider = customFileProvider == null ? new PhysicalFileProvider(contentRootDirFullPath) 	: customFileProvider;");              
                Replace("new WebView2WebViewManager", "CreateWebViewManager");
                Replace("private void StartWebViewCoreIfPossible()", "public virtual WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath,string hostPagePathWithinFileProvider,Action<UrlLoadingEventArgs> externalNavigationStarting,Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing, Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized, ILogger logger)\n\t\t{\n\t\t\treturn new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath, hostPagePathWithinFileProvider, externalNavigationStarting,blazorWebViewInitializing,blazorWebViewInitialized,logger);\n\t\t}\n\t\tprotected void StartWebViewCoreIfPossible()");             
                Replace("BlazorWebView", Path.GetFileNameWithoutExtension(fileName));

                Replace("BlazorWebViewBaseInit", "BlazorWebViewInit");
                Replace("BlazorWebViewFormBaseInit", "BlazorWebViewInit");

                InsertUsing("Microsoft.AspNetCore.Components.Web");
                InsertUsing("Microsoft.AspNetCore.Components.WebView");

                Replace("public string HostPage", "public virtual string HostPage");
                Replace("private bool RequiredStartupPropertiesSet =>", "protected bool RequiredStartupPropertiesSet =>");
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

            if(fileName == "StaticContentHotReloadManager.cs")
            {
                Replace("Microsoft.AspNetCore.Components.WebView.StaticContentHotReloadManager", "PeakSWC.RemoteBlazorWebView.StaticContentHotReloadManager");
                InsertUsing("Microsoft.AspNetCore.Components");
                InsertUsing("Microsoft.AspNetCore.Components.WebView");
            }
        }

        public void WriteAllText(string outputDir)
        {
            File.WriteAllText(Path.Combine(outputDir, fileName), text);
        }

        public void Replace(string oldValue, string newValue)
        {
            text = text.Replace($"{oldValue}", $"{newValue}");
        }

        public void ReplaceFirst(string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0)
            {
                return;
            }
            text = text.Substring(0, pos) + replace + text.Substring(pos + search.Length);
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
