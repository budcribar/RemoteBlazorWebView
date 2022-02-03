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
        string fileName;
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
            if (fileName == "BlazorWebViewFormBase.cs" || fileName == "BlazorWebViewBase.cs")
            {
              
                Replace("var fileProvider = CreateFileProvider(contentRootDirFullPath);", "var customFileProvider = CreateFileProvider(contentRootDirFullPath);\n            IFileProvider fileProvider = customFileProvider == null ? new PhysicalFileProvider(contentRootDirFullPath) 	: customFileProvider;");              
                Replace("new WebView2WebViewManager", "CreateWebViewManager");
                Replace("private void StartWebViewCoreIfPossible()", "public virtual WebView2WebViewManager CreateWebViewManager(WebView2Control webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath)\n\t\t{\n\t\t\treturn new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath);\n\t\t}\n\t\tprotected void StartWebViewCoreIfPossible()");             
                Replace("BlazorWebView", Path.GetFileNameWithoutExtension(fileName));
                InsertUsing("Microsoft.AspNetCore.Components.Web");
                //Replace("WebView2WebViewManager", "WebView2.WebView2WebViewManager");
            }

            Replace("namespace Microsoft.AspNetCore.Components.WebView.WindowsForms", "namespace PeakSWC.RemoteBlazorWebView.WindowsForms");
            Replace("namespace Microsoft.AspNetCore.Components.WebView.Wpf", "namespace PeakSWC.RemoteBlazorWebView.Wpf");

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
