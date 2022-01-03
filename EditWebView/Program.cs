// See https://aka.ms/new-console-template for more information
using EditWebView;

string maui = "maui-6.0.101-preview.11.3";
string inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\WindowsForms");
string outputDir = "../../../../src/RemoteBlazorWebView.WinForms";

if (!Directory.Exists(outputDir))
    throw new Exception("Can't locate output directory");

foreach(var f in Directory.EnumerateFiles(inputDir))
{
    if (f.EndsWith(".csproj")) continue;

    Editor editor = new Editor(f);

    var fileName = Path.GetFileName(f);
    if (fileName == "BlazorWebView.cs")
        if (f.Contains("WindowsForms"))
            fileName = "BlazorWebViewFormBase.cs";
        else
            fileName = "BlazorWebViewBase.cs";


    if (fileName == "BlazorWebViewFormBase.cs" )
    {
        editor.Comment("var assetFileProvider =");
     
        editor.Replace( "? assetFileProvider", "? new PhysicalFileProvider(contentRootDir)");
        editor.Replace("new CompositeFileProvider(customFileProvider, assetFileProvider)", "customFileProvider");
        editor.Replace("new WebView2WebViewManager", "CreateWebViewManager");
        editor.Replace("private void StartWebViewCoreIfPossible()", "public virtual WebView2WebViewManager CreateWebViewManager(WebView2.IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath)\n\t\t{\n\t\t\treturn new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath);\n\t\t}\n\t\tprotected void StartWebViewCoreIfPossible()");
        editor.Replace("BlazorWebView", "BlazorWebViewFormBase");
        editor.InsertUsing("Microsoft.AspNetCore.Components.Web");
        editor.Replace("WebView2WebViewManager", "WebView2.WebView2WebViewManager");
    }

    editor.Replace("namespace Microsoft.AspNetCore.Components.WebView.WindowsForms", "namespace PeakSWC.RemoteBlazorWebView.WindowsForms");


    if (fileName == "RootComponent.cs" || fileName == "WindowsFormsDispatcher.cs" || fileName == "BlazorWebViewFormBase.cs" || fileName=="RootComponentCollectionExtensions.cs")
        editor.InsertUsing("Microsoft.AspNetCore.Components");

    if (fileName == "RootComponent.cs" )
    {
        editor.Replace("WebView2WebViewManager", "WebView2.WebView2WebViewManager");
        editor.InsertUsing("Microsoft.AspNetCore.Components.WebView");
    }

    if (fileName == "RootComponent.cs" || fileName == "BlazorWebViewFormBase.cs")
        editor.Replace("using Microsoft.AspNetCore.Components.WebView.WebView2;", "using WebView2 = Microsoft.AspNetCore.Components.WebView.WebView2;");

    editor.WriteAllText(Path.Combine(outputDir, fileName));
}

inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\Wpf");
outputDir = "../../../../src/RemoteBlazorWebView.Wpf";

if (!Directory.Exists(outputDir))
    throw new Exception("Can't locate output directory");

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

    if (fileName == "BlazorWebViewBase.cs")
    {
        text = text.Replace("var assetFileProvider =", "//var assetFileProvider =");
        text = text.Replace("? assetFileProvider", "? new PhysicalFileProvider(contentRootDir)");
        text = text.Replace("new CompositeFileProvider(customFileProvider, assetFileProvider)", "customFileProvider");

        text = text.Replace("new WebView2WebViewManager", "CreateWebViewManager");
        text = text.Replace("private void StartWebViewCoreIfPossible()", "public virtual WebView2WebViewManager CreateWebViewManager(WebView2.IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, JSComponentConfigurationStore store, string hostPageRelativePath)\n\t\t{\n\t\t\treturn new WebView2WebViewManager(webview, services, dispatcher, fileProvider, store, hostPageRelativePath);\n\t\t}\n\t\tprotected void StartWebViewCoreIfPossible()");
        text = text.Replace("BlazorWebView", "BlazorWebViewBase");
        text = text.Replace("using System;", "using System;\nusing Microsoft.AspNetCore.Components.Web;");
        text = text.Replace("WebView2WebViewManager", "WebView2.WebView2WebViewManager");
    }

    text = text.Replace("namespace Microsoft.AspNetCore.Components.WebView.Wpf", "namespace PeakSWC.RemoteBlazorWebView.Wpf");

    if (fileName == "RootComponent.cs" || fileName == "WpfDispatcher.cs" || fileName == "BlazorWebViewBase.cs" )
        text = text.Replace("using System;", "using System;\nusing Microsoft.AspNetCore.Components;");


    if (fileName == "RootComponent.cs")
    {
        text = text.Replace("WebView2WebViewManager", "WebView2.WebView2WebViewManager");
        text = text.Replace("using System;", "using System;\nusing Microsoft.AspNetCore.Components.WebView;\n");

    }

    if (fileName == "RootComponent.cs" || fileName == "BlazorWebViewBase.cs")
        text = text.Replace("using Microsoft.AspNetCore.Components.WebView.WebView2;", "using WebView2 = Microsoft.AspNetCore.Components.WebView.WebView2;");
    File.WriteAllText(Path.Combine(outputDir, fileName), text);
}
