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

`src/EditWebView` turns the MAUI WPF `OnApplyTemplate` path into `BlazorWebViewBase`. After MAUI [#34869](https://github.com/dotnet/maui/pull/34869) that path binds the template child as `IWebView2` (`UseCompositionControl` defaults to true, so the control is a `WebView2CompositionControl`). The converter sets `webView.AllowExternalDrop = false` on that interface. `IWebView2.AllowExternalDrop` is in the pinned WebView2 package `1.0.4258.31` and maps to `CoreWebView2Controller.AllowExternalDrop`. WinForms still sets `AllowExternalDrop = false` on the `WebView2` constructed in `BlazorWebViewFormBase`.

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

# Performance regression check

`.github/workflows/perf-regression.yml` compares this tree with a baseline ref on the same runner. It runs on every push to `net11` and from the Actions tab (`workflow_dispatch`). The check stays green: over-tolerance rows are `WARN` in the job summary and in the `perf-regression` artifact. A failed sample does not fail the push.

The projects under `src/Benchmarks` (`ClientBenchmark`, `FileRead`, `FilePOC`, `Startup`, `StressServer`) time a live server, a LAN address, or an embedded tutorial executable. They are not what CI runs. CI runs `src/Benchmarks/PerfRegression`, an in-process Kestrel host. It records gRPC stream counts, static-file GET/304 counts, response body bytes, `Cache-Control` bytes, file-provider reads, and bytes read from the desktop file stream. `elapsedMs` and `allocatedBytes` are medians of interleaved repeats. Counts and byte sizes warn above a 5% increase. Time and allocations warn above 20% slower.

Run it locally from the repo root (needs the .NET SDK for this tree and for the baseline, Python 3, and `src/RemoteWebView.Blazor.JS/dist/remote.blazor.desktop.js`):

```console
REPEATS=5 BASELINE=origin/net10 bash scripts/perf-regression.sh
```

Leave `BASELINE` unset to use the newest tag whose `RemoteWebView` target is `net10` or `net11` and that contains `ClientFileSyncManager`. There is no such tag yet, so the script uses `origin/net10`, then `HEAD~1` if that branch is missing. The same knobs are the workflow inputs `baseline`, `repeats`, `time_tolerance` (default `0.20`), and `count_tolerance` (default `0.05`).

```console
TIME_TOLERANCE=0.20 COUNT_TOLERANCE=0.05 BASELINE=origin/net10 bash scripts/perf-regression.sh
```

Results land in `perf-results/summary.md`. The baseline checkout is a temporary git worktree, removed when the script exits. The harness sources are copied into that worktree, so both sides run the same scenario code against their own libraries. The current `remote.blazor.desktop.js` is copied in too; bundle size is not part of the comparison.

`elapsedMs` on these loopback fetches is a few milliseconds, so a ratio over 1.20 can be runner noise. The count and byte columns are the ones that should stay flat. `allocatedBytes` includes Kestrel threads, so it uses the timing tolerance. A scenario that exists on only one side (the cache-provider fetch, on a baseline without that constructor argument) is `SKIP`, not `WARN`. 

