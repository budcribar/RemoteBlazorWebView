using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Linq;
using System.Collections.Generic;

namespace EditWebView
{
    public class Editor
    {
        private string[] lines;
        private readonly string inputFileName;
        private readonly string outputFileName;
        private readonly bool isWindowsForms;

        public Editor(string file)
        {
            if (!File.Exists(file))
                throw new FileNotFoundException($"File not found: {file}");

            lines = File.ReadAllLines(file);
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
            PreserveOriginalStructure();
            ReplaceNamespaces();

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

        private void PreserveOriginalStructure()
        {
            // Ensure copyright notice remains at the top
            int copyrightEndIndex = Array.FindIndex(lines, l => !l.StartsWith("//") && !string.IsNullOrWhiteSpace(l));
            var copyrightLines = lines.Take(copyrightEndIndex).ToList();
            var remainingLines = lines.Skip(copyrightEndIndex).ToList();

            // Preserve order of using statements
            var usingStatements = remainingLines.TakeWhile(l => l.TrimStart().StartsWith("using")).ToList();
            remainingLines = remainingLines.Skip(usingStatements.Count).ToList();

            lines = copyrightLines.Concat(usingStatements).Concat(remainingLines).ToArray();
        }

        private void ReplaceNamespaces()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("namespace "))
                {
                    lines[i] = ReplaceNamespaceInLine(lines[i]);
                }
                else if (!lines[i].TrimStart().StartsWith("using ") && !lines[i].TrimStart().StartsWith("#"))
                {
                    lines[i] = ReplaceNamespacesInCode(lines[i]);
                }
            }
        }

        private string ReplaceNamespaceInLine(string line)
        {
            return line
                .Replace("Microsoft.AspNetCore.Components.WebView.WindowsForms", "PeakSWC.RemoteBlazorWebView.WindowsForms")
                .Replace("Microsoft.AspNetCore.Components.WebView.Wpf", "PeakSWC.RemoteBlazorWebView.Wpf")
                .Replace("Microsoft.AspNetCore.Components.WebView", "PeakSWC.RemoteBlazorWebView");
        }

        private string ReplaceNamespacesInCode(string line)
        {
            return Regex.Replace(line, @"\bMicrosoft\.AspNetCore\.Components\.WebView\b", "PeakSWC.RemoteBlazorWebView");
        }

        private void EditBlazorWebView()
        {
            RenameBlazorWebViewClass();
            EditBlazorWebViewBase();
        }

        private void EditWebView2WebViewManager()
        {
            AddNavigateToStringMethod();
            ReplaceAddMethods();
            MakeClassPublic();
        }

        private void EditUrlLoadingEventArgs()
        {
            ReplaceUrlLoadingStrategy();
            MakeStaticMethodPublic();
        }

        private void EditBlazorWebViewServiceCollectionExtensions()
        {
            ReplaceAddMethods();
            UpdateDeveloperToolsMethod();
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

        private void EditRootComponent()
        {
            // No specific edits needed for RootComponent.cs
        }

        private void EditStaticContentHotReloadManager()
        {
            CorrectMetadataUpdateHandlerAttribute();
        }

        private void RenameBlazorWebViewClass()
        {
            string newClassName = isWindowsForms ? "BlazorWebViewFormBase" : "BlazorWebViewBase";

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = Regex.Replace(lines[i], @"class\s+BlazorWebView\s*:", $"class {newClassName} :");
                lines[i] = Regex.Replace(lines[i], @"\bBlazorWebView\b(?!Base|FormBase)", newClassName);
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
            int index = Array.FindIndex(lines, l => l.Contains("protected override void SendMessage(string message)"));
            if (index != -1)
            {
                lines = lines.Take(index)
                    .Append(method)
                    .Concat(lines.Skip(index))
                    .ToArray();
            }
        }

        private void ReplaceAddMethods()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Replace("AddWindowsFormsBlazorWebView", "AddRemoteWindowsFormsBlazorWebView");
                lines[i] = lines[i].Replace("AddWpfBlazorWebView", "AddRemoteWpfBlazorWebView");
            }
        }

        private void UpdateDeveloperToolsMethod()
        {
            string pattern = @"(public\s+static\s+)?IServiceCollection\s+AddBlazorWebViewDeveloperTools\s*\(this\s+IServiceCollection\s+services\)";
            string replacement = "public static IServiceCollection AddRemoteBlazorWebViewDeveloperTools(this IServiceCollection services)";

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = Regex.Replace(lines[i], pattern, replacement);
            }
        }

        private void MakeClassPublic()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Replace("internal class", "public class");
            }
        }

        private void ReplaceUrlLoadingStrategy()
        {
            string oldStrategy = "var strategy = appOriginUri.IsBaseOf(urlToLoad) ?";
            string newStrategy = @"var split = urlToLoad.AbsolutePath.Split('/');
			var isMirrorUrl = split.Length == 3 && split[1] == ""mirror"" && Guid.TryParse(split[2], out Guid _);
			var strategy = (appOriginUri.IsBaseOf(urlToLoad) || urlToLoad.Scheme == ""data"" || isMirrorUrl) ?";

            int index = Array.FindIndex(lines, l => l.Contains(oldStrategy));
            if (index != -1)
            {
                lines[index] = newStrategy;
            }
        }

        private void MakeStaticMethodPublic()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Replace("internal static", "public static");
            }
        }

        private void CommentOutPragmas()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("#pragma warning disable CA1816") || lines[i].Contains("#pragma warning restore"))
                {
                    lines[i] = "//" + lines[i];
                }
            }
        }

        private void AddAllowExternalDropProperty()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("Dock = DockStyle.Fill,"))
                {
                    lines[i] = lines[i].Replace("Dock = DockStyle.Fill,", "Dock = DockStyle.Fill, AllowExternalDrop = false");
                }
                else if (lines[i].Contains("_webview = (WebView2Control)GetTemplateChild(WebViewTemplateChildName);"))
                {
                    lines[i] += "\n\t\t\t\t_webview.AllowExternalDrop = false;";
                }
            }
        }

        private void AddWebViewManagerProperty()
        {
            string newProperty = "[Browsable(false)]\n        public WebView2WebViewManager WebViewManager => _webviewManager;";
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("public WebView2Control WebView =>"))
                {
                    lines[i] += "\n        " + newProperty;
                    break;
                }
            }
        }

        private void ReplaceCreateWebViewManager()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = lines[i].Replace("new WebView2WebViewManager", "CreateWebViewManager");
            }

            string newMethod = @"public virtual WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath,string hostPagePathWithinFileProvider,Action<UrlLoadingEventArgs> externalNavigationStarting,Action<BlazorWebViewInitializingEventArgs> blazorWebViewInitializing, Action<BlazorWebViewInitializedEventArgs> blazorWebViewInitialized, ILogger logger)
		{
			return new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath, hostPagePathWithinFileProvider, externalNavigationStarting,blazorWebViewInitializing,blazorWebViewInitialized,logger);
		}";

            int index = Array.FindIndex(lines, l => l.Contains("private void StartWebViewCoreIfPossible()"));
            if (index != -1)
            {
                lines = lines.Take(index)
                    .Append(newMethod)
                    .Append("\t\tprotected void StartWebViewCoreIfPossible()")
                    .Concat(lines.Skip(index + 1))
                    .ToArray();
            }
        }

        private void MakeHostPageVirtual()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("public string HostPage") || lines[i].Contains("public string? HostPage"))
                {
                    lines[i] = lines[i].Replace("public string", "public virtual string");
                    lines[i] = lines[i].Replace("public string?", "public virtual string?");
                }
            }
        }

        private void MakeRequiredStartupPropertiesSetProtected()
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("private bool RequiredStartupPropertiesSet =>"))
                {
                    lines[i] = lines[i].Replace("private bool", "protected bool");
                }
            }
        }

        private void CorrectMetadataUpdateHandlerAttribute()
        {
            string pattern = @"\[assembly:\s*MetadataUpdateHandler\(typeof\((Microsoft\.AspNetCore\.Components\.WebView|PeakSWC\.RemoteBlazorWebView)\.StaticContentHotReloadManager\)\)\]";
            string replacement = "[assembly: MetadataUpdateHandler(typeof(PeakSWC.RemoteBlazorWebView.StaticContentHotReloadManager))]";

            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = Regex.Replace(lines[i], pattern, replacement);
            }
        }

        public void WriteAllText(string outputDir)
        {
            string outputPath = Path.Combine(outputDir, outputFileName);
            File.WriteAllLines(outputPath, lines);
        }
    }
}