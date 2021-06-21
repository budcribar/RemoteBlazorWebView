# Building the Javascript

Copy and unzip the preview source files 
copy the files from src/Components/Web.JS to RemoteBlazorWebView\src\RemoteWebWindow.Blazor.JS\upstream\aspnetcore\web.js

`
cd RemoteBlazorWebView\src\RemotePhotino.Blazor.JS\upstream\aspnetcore\web.js
yarn add --dev inspectpack

change package.json

"@microsoft/dotnet-js-interop": "link:../../JSInterop/Microsoft.JSInterop.JS/src",
"@microsoft/signalr": "link:../../SignalR/clients/ts/signalr",
"@microsoft/signalr-protocol-msgpack": "link:../../SignalR/clients/ts/signalr-protocol-msgpack",

to

"@microsoft/signalr": "6.0.0-preview.x",
"@microsoft/signalr-protocol-msgpack":"6.0.0-preview.x",
"@microsoft/dotnet-js-interop": "6.0.0-preview.x",                          // where x is the latest preview

yarn install
yarn run build


Project RemotableWebWindow
copy maui\src\BlazorWebView\src\WebView2IWebView2Wrapper.cs
	





Edit the package.json at RemoteBlazorWebView\src\RemoteWebWindow.Blazor.JS\package.json

copy aspnetcore-6.0.0-preview.5.21301.17\src\Components\WebView\Platforms\WebView2\src\WebView2WebViewManager.cs to RemoteBlazorWebView\src\RemoteableWebWindow

Insert after line 2
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebView;
using Microsoft.AspNetCore.Components.WebView.WebView2;


change namespace Microsoft.AspNetCore.Components.WebView.WebView2 to PeakSWC.RemoteableWebView
Change public class WebView2WebViewManager : WebViewManager to public class WebView2WebViewManager : WebViewManager, IWebViewManager


1. Rename aspnetcore-6.0.0-preview.5.21301.17\src\Components\WebView\Platforms\Wpf\src\BlazorWebView.cs to BlazorWebViewBase.cs

Copy aspnetcore-6.0.0-preview.5.21301.17\src\Components\WebView\Platforms\Wpf\src\*.cs to RemoteBlazorWebView\src\RemoteBlazorWebView.Wpf


   RootComponent.cs

   1. Insert after line 2
   using Microsoft.AspNetCore.Components;
   using PeakSWC.RemoteableWebView;

   2. change namespace Microsoft.AspNetCore.Components.WebView.Wpf to namespace PeakSWC.RemoteBlazorWebView.Wpf
      change public string Selector { get; set; } to public string Selector { get; set; } 
	  change public IDictionary<string, object> Parameters { get; set; } to  public IDictionary<string, object?>? Parameters { get; set; }
	  change internal Task AddToWebViewManagerAsync(WebViewManager webViewManager) to internal Task AddToWebViewManagerAsync(IWebViewManager webViewManager)
	  change internal Task RemoveFromWebViewManagerAsync(WebView2WebViewManager webviewManager)
	  add  if (Selector == null) return Task.CompletedTask; to RemoveFromWebViewManagerAsync


WpfDispather.cs

1. insert using Microsoft.AspNetCore.Components;
2. change namespace Microsoft.AspNetCore.Components.WebView.Wpf to namespace PeakSWC.RemoteBlazorWebView.Wpf
3. change internal sealed class WpfDispatcher : Dispatcher to 

WpfWebView2Wrapper.cs

1. change namespace Microsoft.AspNetCore.Components.WebView.Wpf to PeakSWC.RemoteBlazorWebView.Wpf
2. public WpfWeb2ViewWrapper(WebView2Control webView2) to public WpfWeb2ViewWrapper(WebView2Control? webView2)
3. change public Task EnsureCoreWebView2Async(CoreWebView2Environment environment = null) to public Task EnsureCoreWebView2Async(CoreWebView2Environment? environment = null)

BlazorWebViewBase.cs

1. Insert after line 2
	using Microsoft.AspNetCore.Components;
	using PeakSWC.RemoteableWebView;

2. change namespace Microsoft.AspNetCore.Components.WebView.Wpf to PeakSWC.RemoteBlazorWebView.Wpf

3. Change public sealed class BlazorWebView to public class BlazorWebView 

   Change private WebView2Control _webview; to 
   Change  private WebView2WebViewManager _webviewManager; to WebView2Control? _webview;
   Change   private WebView2WebViewManager _webviewManager; to private IWebViewManager? _webviewManager;

4. add the following method
 public virtual IWebViewManager CreateWebViewManager(IWebView2Wrapper webview, IServiceProvider services, Dispatcher dispatcher, IFileProvider fileProvider, string hostPageRelativePath)
        {
            return new WebView2WebViewManager(webview, services, dispatcher, fileProvider, hostPageRelativePath);
        }

	change  _webviewManager = new WebView2WebViewManager(new WpfWeb2ViewWrapper(_webview), Services, WpfDispatcher.Instance, fileProvider, hostPageRelativePath);
	to 


	 add the nullable to private void HandleRootComponentsCollectionChanged(object sender, NotifyCollectionChangedEventArgs eventArgs)
      
5. Insert around line 151 
	 if (contentRootDir == null) throw new Exception("No root directory found");

	 CHANGE  private void HandleRootComponentsCollectionChanged(object sender, TO private void HandleRootComponentsCollectionChanged(object? sender,

	 CHANGE 
	 var newItems = eventArgs.NewItems.Cast<RootComponent>();
     var oldItems = eventArgs.OldItems.Cast<RootComponent>();

	 TO
	 var newItems = eventArgs.NewItems?.Cast<RootComponent>() ?? new List<RootComponent>();
     var oldItems = eventArgs.OldItems?.Cast<RootComponent>() ?? new List<RootComponent>();

	 change 
	 private void CheckDisposed() to protected
	 

