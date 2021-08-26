# RemoteBlazorWebView.Wpf.BlazorWebView

RemoteBlazorWebView.Wpf.BlazorWebView is based on the .NET 6 Preview 7 Blazor WebView Control for WPF

RemoteBlazorWebView.Wpf.BlazorWebView enables you to interact with the user interface of a program developed with the BlazorWebView WPF control using a web browser. This is accomplished by setting up a server (RemoteableWebViewService) in the cloud and pointing your browser to it.

RemoteBlazorWebView.Wpf is a drop-in replacement for the Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView control and with minimal changes you will be able to remotely control your application.


# RemoteBlazorWebView.WindowsForms.BlazorWebView

RemoteBlazorWebView.WindowsForms.BlazorWebView is based on the .NET 6 Preview 7 Blazor WebView WinForms Control 

RemoteBlazorWebView.WindowsForms.BlazorWebView enables you to interact with the user interface of a program developed with the BlazorWebView WinForms control using a web browser. This is accomplished by setting up a server (RemoteableWebViewService) in the cloud and pointing your browser to it.

RemoteBlazorWebView.WindowsForms.BlazorWebView is a drop-in replacement for the Microsoft.AspNetCore.Components.WebView.WindowsForms.BlazorWebView control and with minimal changes you will be able to remotely control your application.


# Usage instructions

You do not need to build this repo unless you want to customize the RemoteableWebViewService. Run the following command to install the RemoteableWebViewService

```console
dotnet tool update -g PeakSWC.RemoteableWebViewService --version 6.*-* --ignore-failed-sources
```

# Samples

Check out the tutorial at https://github.com/budcribar/RemoteBlazorWebViewTutorial 

