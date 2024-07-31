using System;
using System.IO;
using System.Text.RegularExpressions;

namespace EditWebView
{
    public class Editor
    {
        private string _text;
        private readonly string _fileName;

        public Editor(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                throw new ArgumentException("File path cannot be null or empty.", nameof(file));

            if (!File.Exists(file))
                throw new FileNotFoundException($"File not found: {file}");

            _text = File.ReadAllText(file);
            _fileName = DetermineFileName(file);
        }

        private static string DetermineFileName(string file)
        {
            var fileName = Path.GetFileName(file);

            if (fileName == "BlazorWebView.cs")
            {
                return file.Contains("WindowsForms") ? "BlazorWebViewFormBase.cs" : "BlazorWebViewBase.cs";
            }

            return fileName;
        }

        public void Edit()
        {
            switch (_fileName)
            {
                case "WebView2WebViewManager.cs":
                    EditWebView2WebViewManager();
                    break;
                case "UrlLoadingEventArgs.cs":
                    EditUrlLoadingEventArgs();
                    break;
                case "BlazorWebViewDeveloperTools.cs":
                case "WpfBlazorMarkerService.cs":
                    MakeClassPublic();
                    break;
                case "BlazorWebViewServiceCollectionExtensions.cs":
                    EditBlazorWebViewServiceCollectionExtensions();
                    break;
                case "BlazorWebViewFormBase.cs":
                case "BlazorWebViewBase.cs":
                    EditBlazorWebViewBase();
                    break;
            }

            ReplaceNamespaces();
            InsertUsingStatements();
        }

        private void EditWebView2WebViewManager()
        {
            const string newMethod = @"public void NavigateToString(string htmlContent)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await _webviewReadyTask;
                _webview.NavigateToString(htmlContent);
            });
        }";

            Replace("protected override void SendMessage(string message)", newMethod + "\n        protected override void SendMessage(string message)");
            Replace("using Microsoft.AspNetCore.Components.WebView.WindowsForms;", "using PeakSWC.RemoteBlazorWebView.WindowsForms;");
            Replace("using Microsoft.AspNetCore.Components.WebView.Wpf;", "using PeakSWC.RemoteBlazorWebView.Wpf;");
            Replace("AddWindowsFormsBlazorWebView", "AddRemoteWindowsFormsBlazorWebView");
            Replace("AddWpfBlazorWebView", "AddRemoteWpfBlazorWebView");
            Replace("internal class WebView2WebViewManager", "public class WebView2WebViewManager");
            ReplaceFirst("#elif WEBVIEW2_MAUI", "using Microsoft.AspNetCore.Components.WebView;\nusing Microsoft.AspNetCore.Components;\n#elif WEBVIEW2_MAUI");
        }

        private void EditUrlLoadingEventArgs()
        {
            Replace("var strategy = appOriginUri.IsBaseOf(urlToLoad) ?",
                "var split = urlToLoad.AbsolutePath.Split('/');\n" +
                "\t\t\tvar isMirrorUrl = split.Length == 3 && split[1] == \"mirror\" && Guid.TryParse(split[2], out Guid _);\n" +
                "\t\t\tvar strategy = (appOriginUri.IsBaseOf(urlToLoad) || urlToLoad.Scheme == \"data\" || isMirrorUrl) ?");
            Replace("internal static", "public static");
        }

        private void MakeClassPublic()
        {
            Replace("internal class", "public class");
        }

        private void EditBlazorWebViewServiceCollectionExtensions()
        {
            Replace("using Microsoft.AspNetCore.Components.WebView.WindowsForms;", "using PeakSWC.RemoteBlazorWebView.WindowsForms;");
            Replace("using Microsoft.AspNetCore.Components.WebView.Wpf;", "using PeakSWC.RemoteBlazorWebView.Wpf;");
            Replace("namespace Microsoft.Extensions.DependencyInjection", "namespace PeakSWC.RemoteBlazorWebView");
            Replace("AddWindowsFormsBlazorWebView", "AddRemoteWindowsFormsBlazorWebView");
            Replace("AddWpfBlazorWebView", "AddRemoteWpfBlazorWebView");
            Replace("AddBlazorWebViewDeveloperTools", "AddRemoteBlazorWebViewDeveloperTools");
        }

        private void EditBlazorWebViewBase()
        {
            Comment("#pragma warning disable CA1816");
            Comment("#pragma warning restore");
            Replace("Dock = DockStyle.Fill,", "Dock = DockStyle.Fill, AllowExternalDrop = false");
            Replace("_webview = (WebView2Control)GetTemplateChild(WebViewTemplateChildName);",
                "_webview = (WebView2Control)GetTemplateChild(WebViewTemplateChildName);\n\t\t\t\t_webview.AllowExternalDrop = false;");
            Replace("public WebView2Control WebView => _webview;",
                "public WebView2Control WebView => _webview;\n        [Browsable(false)]\n        public WebView2WebViewManager WebViewManager => _webviewManager;");
            Replace("public WebView2Control WebView => _webview!;",
                "public WebView2Control WebView => _webview!;\n        [Browsable(false)]\n        public WebView2WebViewManager WebViewManager => _webviewManager;");
            Replace("new WebView2WebViewManager", "CreateWebViewManager");
            Replace("private void StartWebViewCoreIfPossible()", CreateWebViewManagerMethod());
            Replace("BlazorWebView", Path.GetFileNameWithoutExtension(_fileName));
            Replace("BlazorWebViewBaseInit", "BlazorWebViewInit");
            Replace("BlazorWebViewFormBaseInit", "BlazorWebViewInit");
            Replace("public string HostPage", "public virtual string? HostPage");
            Replace("public string? HostPage", "public virtual string? HostPage");
            Replace("private bool RequiredStartupPropertiesSet =>", "protected bool RequiredStartupPropertiesSet =>");
        }

        private static string CreateWebViewManagerMethod()
        {
            return @"public virtual WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath,string hostPagePathWithinFileProvider,Action<UrlLoadingEventArgs> externalNavigationStarting,Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing, Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized, ILogger logger)
		{
			return new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath, hostPagePathWithinFileProvider, externalNavigationStarting,blazorWebViewInitializing,blazorWebViewInitialized,logger);
		}
		protected void StartWebViewCoreIfPossible()";
        }

        private void ReplaceNamespaces()
        {
            Replace("namespace Microsoft.AspNetCore.Components.WebView.WindowsForms", "namespace PeakSWC.RemoteBlazorWebView.WindowsForms");
            Replace("namespace Microsoft.AspNetCore.Components.WebView.Wpf", "namespace PeakSWC.RemoteBlazorWebView.Wpf");
            Replace("namespace Microsoft.AspNetCore.Components.WebView.WebView2", "namespace PeakSWC.RemoteBlazorWebView");
            Replace("namespace Microsoft.AspNetCore.Components.WebView", "namespace PeakSWC.RemoteBlazorWebView");
            Replace("using Microsoft.Web.WebView2;", "using Microsoft.Web.WebView2;\nusing Microsoft.AspNetCore.Components.WebView;\nusing Microsoft.AspNetCore.Components;");
        }

        private void InsertUsingStatements()
        {
            if (_fileName == "RootComponent.cs" || _fileName == "WpfDispatcher.cs" || _fileName == "BlazorWebViewBase.cs" ||
                _fileName == "WindowsFormsDispatcher.cs" || _fileName == "BlazorWebViewFormBase.cs" || _fileName == "RootComponentCollectionExtensions.cs")
            {
                InsertUsing("Microsoft.AspNetCore.Components");
            }

            if (_fileName == "RootComponent.cs")
            {
                InsertUsing("Microsoft.AspNetCore.Components.WebView");
            }

            if (_fileName == "RootComponent.cs" || _fileName == "BlazorWebViewFormBase.cs" || _fileName == "BlazorWebViewBase.cs")
            {
                Replace("using Microsoft.AspNetCore.Components.WebView.WebView2;", "using WebView2 = Microsoft.AspNetCore.Components.WebView.WebView2;");
            }

            if (_fileName == "StaticContentHotReloadManager.cs")
            {
                Replace("Microsoft.AspNetCore.Components.WebView.StaticContentHotReloadManager", "PeakSWC.RemoteBlazorWebView.StaticContentHotReloadManager");
                InsertUsing("Microsoft.AspNetCore.Components");
                InsertUsing("Microsoft.AspNetCore.Components.WebView");
            }
        }

        public void WriteAllText(string outputDir)
        {
            File.WriteAllText(Path.Combine(outputDir, _fileName), _text);
        }

        public void Replace(string oldValue, string newValue)
        {
            _text = _text.Replace(oldValue, newValue);
        }

        public void ReplaceFirst(string search, string replace)
        {
            int pos = _text.IndexOf(search);
            if (pos < 0)
            {
                return;
            }
            _text = _text[..pos] + replace + _text[(pos + search.Length)..];
        }

        public void Comment(string target)
        {
            _text = _text.Replace(target, $"//{target}");
        }

        public void InsertUsing(string nameSpace)
        {
            _text = _text.Replace("using System;", $"using System;\nusing {nameSpace};");
        }
    }
}