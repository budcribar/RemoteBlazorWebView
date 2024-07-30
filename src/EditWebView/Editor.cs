using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

namespace EditWebView
{
    public class Editor
    {
        private string text;
        private readonly string inputFileName;
        private readonly string outputFileName;
        private readonly bool isWindowsForms;

        public Editor(string file)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"File not found: {file}");

            text = File.ReadAllText(file);
            inputFileName = Path.GetFileName(file);
            isWindowsForms = file.Contains("WindowsForms");
            outputFileName = DetermineOutputFileName(file);
        }

        private string DetermineOutputFileName(string file)
        {
            var fileName = Path.GetFileName(file);

            if (fileName == "BlazorWebView.cs")
            {
                return isWindowsForms ? "BlazorWebViewFormBase.cs" : "BlazorWebViewBase.cs";
            }

            return fileName;
        }

        public void ApplyEdits()
        {
            ApplyCommonEdits();

            switch (inputFileName)
            {
                case "BlazorWebView.cs":
                    EditBlazorWebView();
                    break;
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
                case "RootComponent.cs":
                    EditRootComponent();
                    break;
                case "StaticContentHotReloadManager.cs":
                    EditStaticContentHotReloadManager();
                    break;
            }
        }

        private void ApplyCommonEdits()
        {
            ReplaceNamespaces();
            InsertUsings();
        }

        private void EditBlazorWebView()
        {
            // Rename the class
            string newClassName = isWindowsForms ? "BlazorWebViewFormBase" : "BlazorWebViewBase";
            text = Regex.Replace(text, @"class\s+BlazorWebView\s*:", $"class {newClassName} :");

            // Update any references to BlazorWebView within the file
            text = Regex.Replace(text, @"\bBlazorWebView\b(?!Base|FormBase)", newClassName);

            // Apply specific edits for BlazorWebView
            CommentOutPragmas();
            AddAllowExternalDropProperty();
            AddWebViewManagerProperty();
            ReplaceCreateWebViewManager();
            MakeHostPageVirtual();
            MakeRequiredStartupPropertiesSetProtected();
        }

        private void EditWebView2WebViewManager()
        {
            AddNavigateToStringMethod();
            ReplaceUsings();
            ReplaceAddMethods();
            MakeClassPublic();
            InsertWebViewUsings();
        }

        private void EditUrlLoadingEventArgs()
        {
            ReplaceUrlLoadingStrategy();
            MakeStaticMethodPublic();
        }

        private void EditBlazorWebViewServiceCollectionExtensions()
        {
            ReplaceUsings();
            ReplaceNamespace("Microsoft.Extensions.DependencyInjection", "PeakSWC.RemoteBlazorWebView");
            ReplaceAddMethods();
            UpdateDeveloperToolsMethod();
        }

        private void EditRootComponent()
        {
            ReplaceOrInsertWebView2Alias();
        }

        private void EditStaticContentHotReloadManager()
        {
            CorrectMetadataUpdateHandlerAttribute();
            InsertUsings();
        }

        private void ReplaceNamespaces()
        {
            ReplaceNamespace("Microsoft.AspNetCore.Components.WebView.WindowsForms", "PeakSWC.RemoteBlazorWebView.WindowsForms");
            ReplaceNamespace("Microsoft.AspNetCore.Components.WebView.Wpf", "PeakSWC.RemoteBlazorWebView.Wpf");
            ReplaceNamespace("Microsoft.AspNetCore.Components.WebView.WebView2", "PeakSWC.RemoteBlazorWebView");
            ReplaceNamespace("Microsoft.AspNetCore.Components.WebView", "PeakSWC.RemoteBlazorWebView");
        }

        public void ReplaceNamespace(string oldNamespace, string newNamespace)
        {
            string pattern = $@"namespace\s+{Regex.Escape(oldNamespace)}(\s|{{)";
            text = Regex.Replace(text, pattern, $"namespace {newNamespace}$1");
        }

        private void InsertUsings()
        {
            var usingsToInsert = new List<string>();

            // Always add System first if it's not already present
            if (!text.Contains("using System;"))
            {
                usingsToInsert.Add("System");
            }

            if (inputFileName == "RootComponent.cs" ||
                inputFileName == "WpfDispatcher.cs" ||
                inputFileName == "BlazorWebView.cs" ||
                inputFileName == "WindowsFormsDispatcher.cs" ||
                inputFileName == "RootComponentCollectionExtensions.cs")
            {
                usingsToInsert.Add("Microsoft.AspNetCore.Components.WebView");
                usingsToInsert.Add("Microsoft.AspNetCore.Components");
            }

            if (inputFileName == "StaticContentHotReloadManager.cs")
            {
                usingsToInsert.Add("Microsoft.AspNetCore.Components.WebView");
                usingsToInsert.Add("Microsoft.AspNetCore.Components");
            }

            if (usingsToInsert.Count > 0)
            {
                InsertUsingsAfterCopyright(usingsToInsert);
            }
        }

        private void InsertUsingsAfterCopyright(List<string> usingsToInsert)
        {
            int copyrightEnd = text.IndexOf("*/");
            if (copyrightEnd == -1)
            {
                copyrightEnd = text.IndexOf("using ");
            }
            else
            {
                copyrightEnd += 2; // Move past the */
            }

            if (copyrightEnd != -1)
            {
                string existingUsings = text.Substring(copyrightEnd);
                var allUsings = new List<string>();

                // Add existing using statements
                foreach (var line in existingUsings.Split('\n'))
                {
                    if (line.TrimStart().StartsWith("using "))
                    {
                        allUsings.Add(line.Trim());
                    }
                    else
                    {
                        break;
                    }
                }

                // Add new using statements
                foreach (var newUsing in usingsToInsert)
                {
                    string usingStatement = $"using {newUsing};";
                    if (!allUsings.Contains(usingStatement))
                    {
                        allUsings.Add(usingStatement);
                    }
                }

                // Sort the using statements, keeping System first
                allUsings = allUsings.OrderBy(u => u == "using System;" ? 0 : 1)
                                     .ThenBy(u => u)
                                     .ToList();

                string newUsingBlock = string.Join("\n", allUsings);
                text = text.Substring(0, copyrightEnd) + "\n" + newUsingBlock + "\n" + text.Substring(text.IndexOf('\n', copyrightEnd + 1) + 1);
            }
            else
            {
                string newUsings = string.Join("\n", usingsToInsert.Select(u => $"using {u};"));
                text = newUsings + "\n" + text;
            }
        }

        private void InsertWebViewUsings()
        {
            InsertUsing("Microsoft.AspNetCore.Components.WebView");
            InsertUsing("Microsoft.AspNetCore.Components");
        }

        private void InsertUsing(string nameSpace)
        {
            if (!text.Contains($"using {nameSpace};"))
            {
                int lastUsingIndex = text.LastIndexOf("using ", StringComparison.Ordinal);
                if (lastUsingIndex != -1)
                {
                    int endOfLine = text.IndexOf('\n', lastUsingIndex);
                    if (endOfLine != -1)
                    {
                        text = text.Insert(endOfLine + 1, $"using {nameSpace};\n");
                        return;
                    }
                }

                text = $"using {nameSpace};\n" + text;
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

        private void UpdateDeveloperToolsMethod()
        {
            string pattern = @"(public\s+static\s+)?IServiceCollection\s+AddBlazorWebViewDeveloperTools\s*\(this\s+IServiceCollection\s+services\)";
            string replacement = "public static IServiceCollection AddRemoteBlazorWebViewDeveloperTools(this IServiceCollection services)";

            text = Regex.Replace(text, pattern, replacement);
        }

        private void MakeClassPublic()
        {
            Replace("internal class", "public class");
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

        private void ReplaceOrInsertWebView2Alias()
        {
            string webView2Alias = "using WebView2 = Microsoft.AspNetCore.Components.WebView.WebView2;";

            if (text.Contains("using Microsoft.AspNetCore.Components.WebView.WebView2;"))
            {
                text = text.Replace("using Microsoft.AspNetCore.Components.WebView.WebView2;", webView2Alias);
            }
            else if (!text.Contains(webView2Alias))
            {
                InsertUsing(webView2Alias);
            }
        }

        private void CorrectMetadataUpdateHandlerAttribute()
        {
            string pattern = @"\[assembly:\s*MetadataUpdateHandler\(typeof\((Microsoft\.AspNetCore\.Components\.WebView|PeakSWC\.RemoteBlazorWebView)\.StaticContentHotReloadManager\)\)\]";
            string replacement = "[assembly: MetadataUpdateHandler(typeof(PeakSWC.RemoteBlazorWebView.StaticContentHotReloadManager))]";

            text = Regex.Replace(text, pattern, replacement);
        }

        public void WriteAllText(string outputDir)
        {
            File.WriteAllText(Path.Combine(outputDir, outputFileName), text);
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
    }
}