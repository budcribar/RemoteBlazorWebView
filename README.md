# RemoteBlazorWebView

RemoteBlazorWebView is based on the .NET 6 Blazor WebView Control for WPF

RemoteBlazorWebView enables you to interact with the user interface of a program developed with the BlazorWebView WPF control using a web browser. This is accomplished by setting up a server (RemoteableWebService) in the cloud and pointing your browser to it.

RemoteBlazorWebView is a drop-in replacement for the BlazorWebView WPF control and with minimal changes you will be able to remotely control your application.


# Usage instructions

You do not need to build this repo unless you want to customize the RemoteableWebWindowService. Run the following command to install the RemoteableWebWindowService

```console
dotnet tool update -g PeakSWC.RemoteableWebViewService --version 0.*-*
```

# Samples

Check out the tutorial at https://github.com/budcribar/RemoteBlazorWebViewTutorial 

