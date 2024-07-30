using System;
using System.IO;
using System.Text.RegularExpressions;

namespace EditWebView
{
    public class Editor
    {
        private string text;
        private readonly string fileName;

        public Editor(string file)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"File not found: {file}");

            text = File.ReadAllText(file);
            fileName = DetermineFileName(file);
        }

        public Editor(string content, string fileName)
        {
            text = content;
            this.fileName = DetermineFileName(fileName);
        }

        private string DetermineFileName(string file)
        {
            var fileName = Path.GetFileName(file);

            if (fileName == "BlazorWebView.cs")
            {
                return file.Contains("WindowsForms") ? "BlazorWebViewFormBase.cs" : "BlazorWebViewBase.cs";
            }

            return fileName;
        }

        public void ApplyEdits()
        {
            ApplyCommonEdits();

            switch (fileName)
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
        }

        private void ApplyCommonEdits()
        {
            ReplaceNamespaces();
            InsertUsings();  // This will now be more selective

            if (fileName == "WebView2WebViewManager.cs")
            {
                InsertWebViewUsing();
            }
        }

       
        private void EditWebView2WebViewManager()
        {
            AddNavigateToStringMethod();
            ReplaceUsings();
            ReplaceAddMethods();
            MakeClassPublic();
            InsertWebViewUsing();
        }

        private void EditUrlLoadingEventArgs()
        {
            ReplaceUrlLoadingStrategy();
            MakeStaticMethodPublic();
        }

        public void ReplaceNamespace(string oldNamespace, string newNamespace)
        {
            string pattern = $@"namespace\s+{Regex.Escape(oldNamespace)}(\s|{{)";
            text = Regex.Replace(text, pattern, $"namespace {newNamespace}$1");
        }


        private void EditBlazorWebViewServiceCollectionExtensions()
        {
            ReplaceUsings();
            ReplaceNamespace("Microsoft.Extensions.DependencyInjection", "PeakSWC.RemoteBlazorWebView");
            ReplaceAddMethods();
        }

        private void EditBlazorWebViewBase()
        {
            CommentOutPragmas();
            AddAllowExternalDropProperty();
            AddWebViewManagerProperty();
            ReplaceCreateWebViewManager();
            MakeHostPageVirtual();
            MakeRequiredStartupPropertiesSetProtected();
        }

        private void ReplaceNamespaces()
        {
            Replace("namespace Microsoft.AspNetCore.Components.WebView.WindowsForms", "namespace PeakSWC.RemoteBlazorWebView.WindowsForms");
            Replace("namespace Microsoft.AspNetCore.Components.WebView.Wpf", "namespace PeakSWC.RemoteBlazorWebView.Wpf");
            Replace("namespace Microsoft.AspNetCore.Components.WebView.WebView2", "namespace PeakSWC.RemoteBlazorWebView");
            Replace("namespace Microsoft.AspNetCore.Components.WebView", "namespace PeakSWC.RemoteBlazorWebView");
        }

        private void InsertUsings()
        {
            if (fileName == "RootComponent.cs" ||
                fileName == "WpfDispatcher.cs" ||
                fileName == "BlazorWebViewBase.cs" ||
                fileName == "WindowsFormsDispatcher.cs" ||
                fileName == "BlazorWebViewFormBase.cs" ||
                fileName == "RootComponentCollectionExtensions.cs")
            {
                InsertUsing("Microsoft.AspNetCore.Components");
            }

            if (fileName == "RootComponent.cs" ||
                fileName == "BlazorWebViewFormBase.cs" ||
                fileName == "BlazorWebViewBase.cs")
            {
                InsertUsing("Microsoft.AspNetCore.Components.WebView");
            }

            if (fileName == "StaticContentHotReloadManager.cs")
            {
                InsertUsing("Microsoft.AspNetCore.Components");
                InsertUsing("Microsoft.AspNetCore.Components.WebView");
            }
        }

        private void AddNavigateToStringMethod()
        {
            string method = @"
        public void NavigateToString(string htmlContent)
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await _webviewReadyTask;
                _webview.NavigateToString(htmlContent);
            });
        }";
            Replace("protected override void SendMessage(string message)", method + "\n        protected override void SendMessage(string message)");
        }

        private void ReplaceUsings()
        {
            Replace("using Microsoft.AspNetCore.Components.WebView.WindowsForms;", "using PeakSWC.RemoteBlazorWebView.WindowsForms;");
            Replace("using Microsoft.AspNetCore.Components.WebView.Wpf;", "using PeakSWC.RemoteBlazorWebView.Wpf;");
        }

        private void ReplaceAddMethods()
        {
            Replace("AddWindowsFormsBlazorWebView", "AddRemoteWindowsFormsBlazorWebView");
            Replace("AddWpfBlazorWebView", "AddRemoteWpfBlazorWebView");
        }

        private void MakeClassPublic()
        {
            Replace("internal class", "public class");
        }

        private void InsertWebViewUsing()
        {
            ReplaceFirst("#elif WEBVIEW2_MAUI", "using Microsoft.AspNetCore.Components.WebView;\nusing Microsoft.AspNetCore.Components;\n#elif WEBVIEW2_MAUI");
        }

        private void ReplaceUrlLoadingStrategy()
        {
            Replace("var strategy = appOriginUri.IsBaseOf(urlToLoad) ?",
                "var split = urlToLoad.AbsolutePath.Split('/');\n" +
                "			var isMirrorUrl = split.Length == 3 && split[1] == \"mirror\" && Guid.TryParse(split[2], out Guid _);\n" +
                "			var strategy = (appOriginUri.IsBaseOf(urlToLoad) || urlToLoad.Scheme == \"data\" || isMirrorUrl) ?");
        }

        private void MakeStaticMethodPublic()
        {
            Replace("internal static", "public static");
        }

        private void CommentOutPragmas()
        {
            Comment("#pragma warning disable CA1816");
            Comment("#pragma warning restore");
        }

        private void AddAllowExternalDropProperty()
        {
            Replace("Dock = DockStyle.Fill,", "Dock = DockStyle.Fill, AllowExternalDrop = false");
            Replace("_webview = (WebView2Control)GetTemplateChild(WebViewTemplateChildName);",
                "_webview = (WebView2Control)GetTemplateChild(WebViewTemplateChildName);\n\t\t\t\t_webview.AllowExternalDrop = false;");
        }

        private void AddWebViewManagerProperty()
        {
            Replace("public WebView2Control WebView => _webview;",
                "public WebView2Control WebView => _webview;\n        [Browsable(false)]\n        public WebView2WebViewManager WebViewManager => _webviewManager;");
            Replace("public WebView2Control WebView => _webview!;",
                "public WebView2Control WebView => _webview!;\n        [Browsable(false)]\n        public WebView2WebViewManager WebViewManager => _webviewManager;");
        }

        private void ReplaceCreateWebViewManager()
        {
            Replace("new WebView2WebViewManager", "CreateWebViewManager");
            Replace("private void StartWebViewCoreIfPossible()",
                "public virtual WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath,string hostPagePathWithinFileProvider,Action<UrlLoadingEventArgs> externalNavigationStarting,Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing, Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized, ILogger logger)\n" +
                "\t\t{\n" +
                "\t\t\treturn new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath, hostPagePathWithinFileProvider, externalNavigationStarting,blazorWebViewInitializing,blazorWebViewInitialized,logger);\n" +
                "\t\t}\n" +
                "\t\tprotected void StartWebViewCoreIfPossible()");
        }

        private void MakeHostPageVirtual()
        {
            Replace("public string HostPage", "public virtual string? HostPage");
            Replace("public string? HostPage", "public virtual string? HostPage");
        }

        private void MakeRequiredStartupPropertiesSetProtected()
        {
            Replace("private bool RequiredStartupPropertiesSet =>", "protected bool RequiredStartupPropertiesSet =>");
        }

        public void WriteAllText(string outputPath)
        {
            File.WriteAllText(outputPath, text);
        }

        public void Replace(string oldValue, string newValue)
        {
            text = text.Replace(oldValue, newValue);
        }

        public void ReplaceFirst(string search, string replace)
        {
            int pos = text.IndexOf(search);
            if (pos < 0) return;
            text = text[..pos] + replace + text[(pos + search.Length)..];
        }

        public void Comment(string target)
        {
            text = text.Replace(target, $"//{target}");
        }

        private void InsertUsing(string nameSpace)
        {
            if (!text.Contains($"using {nameSpace};"))
            {
                text = Regex.Replace(text, @"(using System;)", $"$1\nusing {nameSpace};");
            }
        }
    }
}