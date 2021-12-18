// See https://aka.ms/new-console-template for more information

string inputDir = @"C:\Users\budcr\Downloads\maui-6.0.101-preview.11.3\src\BlazorWebView\src\WindowsForms";
string outputDir = "winforms";

if(!Directory.Exists(outputDir))
    Directory.CreateDirectory(outputDir);

foreach(var f in Directory.EnumerateFiles(inputDir))
{
    if (f.EndsWith(".csproj")) continue;
   
    var text = File.ReadAllText(f);

    var fileName = Path.GetFileName(f);
    if (fileName == "BlazorWebView.cs")
        if (f.Contains("WindowsForms"))
            fileName = "BlazorWebViewFormBase.cs";
        else
            fileName = "BlazorWebViewBase.cs";

    //if (fileName == "RootComponentsCollection.cs") continue;

    if (fileName == "BlazorWebViewFormBase.cs")
    {
        text = text.Replace("private void StartWebViewCoreIfPossible()", "public virtual WebView2WebViewManager CreateWebViewManager(WebView2.IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath)\n{\nreturn new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath);\n}protected void StartWebViewCoreIfPossible()");
        text = text.Replace("BlazorWebView", "BlazorWebViewFormBase");
        text = text.Replace("new WebView2WebViewManager", "CreateWebViewManager");

        text = text.Replace("using System;", "using System;\nusing Microsoft.AspNetCore.Components.Web;");
        text = text.Replace("WebView2WebViewManager", "RemoteWebView.WebView2WebViewManager");

    }
    text = text.Replace("namespace Microsoft.AspNetCore.Components.WebView.WindowsForms", "namespace PeakSWC.RemoteBlazorWebView.WindowsForms");

    if (fileName != "WindowsFormsWebView2Wrapper.cs")
        text = text.Replace("using System;", "using System;\nusing Microsoft.AspNetCore.Components;");

    if (fileName != "WindowsFormsDispatcher.cs" && fileName != "WindowsFormsWebView2Wrapper.cs")
        text = text.Replace("using System;", "using System;\nusing PeakSWC.RemoteWebView;\nusing Microsoft.AspNetCore.Components.WebView;\n");

    if(fileName != "RootComponent.cs" && fileName != "WindowsFormsDispatcher.cs" && fileName != "WindowsFormsWebView2Wrapper.cs")
        text = text.Replace("using System;", "using System;\nusing WebView2 = Microsoft.AspNetCore.Components.WebView.WebView2;\n");
    
    if (fileName == "RootComponent.cs" || fileName == "BlazorWebViewFormBase.cs")
       text = text.Replace("using Microsoft.AspNetCore.Components.WebView.WebView2;", "");
    File.WriteAllText(Path.Combine(outputDir, fileName), text);
}

inputDir = @"C:\Users\budcr\Downloads\maui-6.0.101-preview.11.3\src\BlazorWebView\src\Wpf";
outputDir = "wpf";

if (!Directory.Exists(outputDir))
    Directory.CreateDirectory(outputDir);

foreach (var f in Directory.EnumerateFiles(inputDir))
{
    if (f.EndsWith(".csproj")) continue;

    var text = File.ReadAllText(f);

    var fileName = Path.GetFileName(f);
    if (fileName == "BlazorWebView.cs")
        if (f.Contains("WindowsForms"))
            fileName = "BlazorWebViewFormBase.cs";
        else
            fileName = "BlazorWebViewBase.cs";

    //if (fileName == "RootComponentsCollection.cs") continue;

    if (fileName == "BlazorWebViewBase.cs")
    {
        text = text.Replace("private void StartWebViewCoreIfPossible()", "public virtual WebView2WebViewManager CreateWebViewManager(WebView2.IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath)\n{\nreturn new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath);\n}protected void StartWebViewCoreIfPossible()");
        text = text.Replace("BlazorWebView", "BlazorWebViewBase");
        text = text.Replace("new WebView2WebViewManager", "CreateWebViewManager");

        text = text.Replace("using System;", "using System;\nusing Microsoft.AspNetCore.Components.Web;");
        text = text.Replace("WebView2WebViewManager", "RemoteWebView.WebView2WebViewManager");

    }
    text = text.Replace("namespace Microsoft.AspNetCore.Components.WebView.Wpf", "namespace PeakSWC.RemoteBlazorWebView.Wpf");

 
    text = text.Replace("using System;", "using System;\nusing PeakSWC.RemoteWebView;\nusing Microsoft.AspNetCore.Components;\nusing Microsoft.AspNetCore.Components.WebView;\nusing WebView2 = Microsoft.AspNetCore.Components.WebView.WebView2;\n");
    //text = text.Replace("Microsoft.AspNetCore.Components.WebView", "PeakSWC.RemoteBlazorWebView");

    if (fileName == "RootComponent.cs" || fileName == "BlazorWebViewBase.cs")
        text = text.Replace("using Microsoft.AspNetCore.Components.WebView.WebView2;", "");
    File.WriteAllText(Path.Combine(outputDir, fileName), text);
}
