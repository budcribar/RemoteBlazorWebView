# RemoteBlazorWebView.Wpf.BlazorWebView

RemoteBlazorWebView.Wpf.BlazorWebView is a powerful control based on the .NET 11 Blazor WebView Control for WPF applications. It allows you to interact with the user interface of a program developed using the BlazorWebView WPF control through a web browser by leveraging a cloud-based server.

The RemoteBlazorWebView.Wpf.BlazorWebView control facilitates remote interaction with your application's user interface by setting up a server (RemoteWebViewService) in the cloud and connecting your browser to it.

As a drop-in replacement for the Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView control, RemoteBlazorWebView.Wpf requires only minimal changes to your existing application to enable remote control capabilities. This makes it an efficient and convenient solution for extending your application's functionality.


# RemoteBlazorWebView.WindowsForms.BlazorWebView

RemoteBlazorWebView.WindowsForms.BlazorWebView is a robust control built on the .NET 11 Blazor WebView WinForms Control, designed for Windows Forms applications. It allows you to engage with the user interface of a program created using the BlazorWebView WinForms control through a web browser, utilizing a cloud-based server.

The RemoteBlazorWebView.WindowsForms.BlazorWebView control enables remote interaction with your application's user interface by establishing a server (RemoteWebViewService) in the cloud and directing your browser to connect with it.

As a seamless replacement for the Microsoft.AspNetCore.Components.WebView.WindowsForms.BlazorWebView control, RemoteBlazorWebView.WindowsForms.BlazorWebView necessitates only minimal adjustments to your current application to empower remote control capabilities. This makes it an effective and user-friendly option for enhancing your application's features.

# Demo Video
![RemoteBlazorWebView](https://raw.githubusercontent.com/budcribar/RemoteBlazorWebView/net11/icons/RemoteBlazorWebView.gif)

# Usage instructions

Run RemoteWebViewService from this repo:

```console
dotnet run --project src/RemoteWebViewService
```

# Samples

Check out the tutorial at https://github.com/budcribar/RemoteBlazorWebViewTutorial

# Build notes

StressServer embeds the tutorial's Windows publish output, `RemoteBlazorWebViewTutorial.WpfApp/bin/publishEmbedded/RemoteBlazorWebViewTutorial.WpfApp.exe`. That exe comes from a Windows publish, so StressServer cannot build on Linux.

# Static content caching

WPF, WinForms, and Photino `BlazorWebView` windows expose `StaticContentCacheControlProvider`. It is the remote counterpart of the .NET MAUI 11 opt-in (`BlazorWebView.StaticContentCacheControlProvider`, [dotnet/maui#35706](https://github.com/dotnet/maui/pull/35706)). Set it before the browser requests static files:

```csharp
blazorWebView.StaticContentCacheControlProvider = request =>
    request.Uri.AbsolutePath.StartsWith("/images/catalog/", StringComparison.Ordinal) &&
    request.ContentType.StartsWith("image/", StringComparison.Ordinal)
        ? "public, max-age=86400"
        : null;
```

The callback receives an absolute URI on the app host (`https://0.0.0.1/...`, or `https://0.0.0.0/...` when the `BlazorWebView.AppHostAddressAlways0000` switch is on) and the content type the service will send. The query string is included, so `image.png?v=2` is visible on `request.Uri`.

- Property not set: remote responses keep the previous headers. There is no `Cache-Control` unless client caching is enabled, in which case the response is `public,max-age=3600` plus `ETag` and `Last-Modified`.
- Returns null, empty, or whitespace: `no-cache, max-age=0, must-revalidate, no-store`.
- Throws, returns a CR or LF, or returns more than 1024 characters: the same no-store default. MAUI does not cap the string; the cap is only on this remote path, because the value is copied into the HTTP response.
- Any other return value becomes the response `Cache-Control`, including on a 304 when client caching is enabled. It replaces `public,max-age=3600`. `ETag` and `Last-Modified` are still sent only when client caching is enabled.

Kestrel may reorder `Cache-Control` directives as it writes the response. Only GET serves the file. HEAD, POST, and other methods are not given this header. A `Range` header does not change the response: the server still returns 200 and the full body. This path does not implement MAUI's in-WebView response cache. 

