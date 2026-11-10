# RemoteBlazorWebView.Wpf.BlazorWebView

RemoteBlazorWebView.Wpf.BlazorWebView is a powerful control based on the .NET 9 Blazor WebView Control for WPF applications. It allows you to interact with the user interface of a program developed using the BlazorWebView WPF control through a web browser by leveraging a cloud-based server.

The RemoteBlazorWebView.Wpf.BlazorWebView control facilitates remote interaction with your application's user interface by setting up a server (RemoteWebViewService) in the cloud and connecting your browser to it.

As a drop-in replacement for the Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView control, RemoteBlazorWebView.Wpf requires only minimal changes to your existing application to enable remote control capabilities. This makes it an efficient and convenient solution for extending your application's functionality.


# RemoteBlazorWebView.WindowsForms.BlazorWebView

RemoteBlazorWebView.WindowsForms.BlazorWebView is a robust control built on the .NET 9 Blazor WebView WinForms Control, designed for Windows Forms applications. It allows you to engage with the user interface of a program created using the BlazorWebView WinForms control through a web browser, utilizing a cloud-based server.

The RemoteBlazorWebView.WindowsForms.BlazorWebView control enables remote interaction with your application's user interface by establishing a server (RemoteWebViewService) in the cloud and directing your browser to connect with it.

As a seamless replacement for the Microsoft.AspNetCore.Components.WebView.WindowsForms.BlazorWebView control, RemoteBlazorWebView.WindowsForms.BlazorWebView necessitates only minimal adjustments to your current application to empower remote control capabilities. This makes it an effective and user-friendly option for enhancing your application's features.

# Demo Video
![RemoteBlazorWebView](https://raw.githubusercontent.com/budcribar/RemoteBlazorWebView/net9/icons/RemoteBlazorWebView.gif)

# net9 package 9.0.23

The packages on this line are version 9.0.23. Build them with SDK 9.0.321 (`rollForward`: `latestMinor`). The target frameworks stay `net9.0`, `net9.0-windows`, and `net9`. Direct Microsoft .NET 9 servicing references are 9.0.23. `Microsoft.AspNetCore.Components.WebView.WindowsForms` stays at 9.0.120 and still restores `Microsoft.AspNetCore.Components.WebView` 9.0.9.

.NET 9 end of support is November 10, 2026. 9.0.23 is the November 10, 2026 final servicing release (SDK 9.0.321). Details are in [doc/CHANGELOG.md](doc/CHANGELOG.md).

# Usage instructions

You do not need to build this repo unless you want to customize the RemoteWebViewService. After 9.0.23 is published, install the tool with:

```console
dotnet tool update -g PeakSWC.RemoteWebViewService --version 9.0.23
```

# Samples

Check out the tutorial at https://github.com/budcribar/RemoteBlazorWebViewTutorial 

