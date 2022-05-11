# RemoteBlazorWebView.Wpf.BlazorWebView

RemoteBlazorWebView.Wpf.BlazorWebView is based on the .NET 7 Blazor WebView Control for WPF

RemoteBlazorWebView.Wpf.BlazorWebView enables you to interact with the user interface of a program developed with the BlazorWebView WPF control using a web browser. This is accomplished by setting up a server (RemoteWebViewService) in the cloud and pointing your browser to it.

RemoteBlazorWebView.Wpf is a drop-in replacement for the Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView control and with minimal changes you will be able to remotely control your application.


# RemoteBlazorWebView.WindowsForms.BlazorWebView

RemoteBlazorWebView.WindowsForms.BlazorWebView is based on the .NET 7 Blazor WebView WinForms Control 

RemoteBlazorWebView.WindowsForms.BlazorWebView enables you to interact with the user interface of a program developed with the BlazorWebView WinForms control using a web browser. This is accomplished by setting up a server (RemoteWebViewService) in the cloud and pointing your browser to it.

RemoteBlazorWebView.WindowsForms.BlazorWebView is a drop-in replacement for the Microsoft.AspNetCore.Components.WebView.WindowsForms.BlazorWebView control and with minimal changes you will be able to remotely control your application.

# Demo Video
![RemoteBlazorWebView](./RemoteBlazorWebView.gif)

# Usage instructions

You do not need to build this repo unless you want to customize the RemoteWebViewService. Run the following command to install the RemoteWebViewService

*** Note The recent name change from RemoteableWebViewService to RemoteWebViewService

```console
dotnet tool update -g PeakSWC.RemoteWebViewService --version 7.*-* 
```

# Samples

Check out the tutorial at https://github.com/budcribar/RemoteBlazorWebViewTutorial 

