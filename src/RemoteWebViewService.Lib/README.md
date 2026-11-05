# PeakSWC.RemoteWebViewService.Lib

The RemoteWebViewService as a library. The same code that runs as `RemoteWebViewService.exe` (and in the
Railway container) can be hosted inside your own process, for example a WPF or WinForms app that shows its
RemoteBlazorWebView UI in a local browser or a WebView2 iframe without a separate server process.

## Embedded (in-process)

```csharp
using PeakSWC.RemoteWebView;

// Loopback, plain HTTP, free ports. Returns once Kestrel is listening; does not block.
await using var server = await RemoteWebViewServer.StartAsync(new RemoteWebViewServerOptions
{
    MaxClients = 1,
    FrameAncestors = ["'self'", "https://host.example"],   // optional
    ConfigureLogging = lb => lb.AddDebug(),                 // optional; default is silent
});

blazorWebView.ServerUri = server.BaseUri;      // browsers: http://127.0.0.1:<port>/
blazorWebView.GrpcBaseUri = server.GrpcUri;    // native gRPC client: http://127.0.0.1:<grpcPort>/
// The page for a client is new Uri(server.BaseUri, $"app/{blazorWebView.Id}")

await server.StopAsync();                      // or dispose
```

- `Port = 0` (the default) binds a free port; read the real one from `server.Port` / `server.BaseUri`.
- Plain HTTP is allowed only on a loopback address, so no development certificate is needed. Without TLS
  there is no ALPN, so browsers (HTTP/1.1, gRPC-Web) and the native gRPC client (HTTP/2) get separate ports.
  Set `UseHttps = true` (dev certificate) or `Certificate` to serve both on one HTTPS port.
- The embedded host does not change the current directory or thread pool, does not read the host app's
  `appsettings.json` or environment variables, and does not hook Ctrl+C or process exit.
- `MaxClients` limits concurrent RemoteWebView clients (a new `CreateWebView` gets 503 / `Unavailable`);
  browser requests for connected clients are never blocked. With `MaxClients = 1` the client-list and
  file-watcher services are not mapped.
- `FrameAncestors` adds `Content-Security-Policy: frame-ancestors ...` to HTML responses and rejects
  cross-origin script calls whose `Origin` is not listed. Same-origin calls are always allowed.

## Standalone

```csharp
RemoteWebViewServer.Run(args);   // what RemoteWebViewService.exe does
RemoteWebViewServer.Run(5001, maxNumClients: 1, frameAncestors: ["'self'"]);   // blocking, HTTPS on loopback
```
