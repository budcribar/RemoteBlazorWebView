# Remote components

Run a Blazor component in its own process and show it inside your app, in a sandboxed iframe. Typical use: UI from code you don't
fully trust (a plugin), run under a restricted token, that must not be able to touch the app's page.

```
 app process                                        component process (RemoteComponentHost)
 ┌──────────────────────────────┐   stdin: "set"    ┌──────────────────────────────┐
 │ <MyFrame Open=... Value=...> │ ────────────────▶ │ MyReceiver (contract values) │
 │   iframe sandbox, opaque     │ ◀──────────────── │ root component (your UI)     │
 │   origin, height only        │  stdout: "call"   │ RemoteBlazorWebView client   │
 └──────────────┬───────────────┘                   └──────────────┬───────────────┘
                └──── iframe src = <relay>/<id> ◀── relay ◀────────┘
```

- **Data goes over the process's stdin/stdout**, newline-delimited JSON (`RemoteComponentMessage`): the app sends `set` with the
  contract values that changed, the component sends `call` for a callback. Nothing crosses through the browser, so in a remote
  session the technician's browser can't forge either side.
- **The iframe** is sandboxed without `allow-same-origin` (`RemoteComponentFrame.DefaultSandbox`), so it has an opaque origin. The
  only `postMessage` the app's page accepts from it is its height (`rbwv-remote-height`), matched by the sending window.

## The pieces

| Side | Type | Package |
|---|---|---|
| App | `RemoteComponentFrameBase`: the iframe, sends values, raises callbacks. `Open` starts the process and returns a `RemoteComponentSession` (the frame's address and the `RemoteComponentConnection` to the process) | this one |
| Component process | `RemoteComponentHost.Run(options)`: the never-shown WPF window with a RemoteBlazorWebView client of the relay; exits when the frame closes or reloads, or the app exits | PeakSWC.RemoteBlazorWebView.Wpf |
| Component process | `RemoteComponentReceiverBase`: holds the values the app sent, raises `Changed`, sends callbacks; `services.AddRemoteComponentReceiver<T>(connection)` | this one |

How the app starts the process (token, folder, relay) is up to it: `Open` is a function you pass to the frame. The relay is usually
an embedded one (`PeakSWC.RemoteWebViewService.Lib`) with `FrameAncestors` set to the app's origin and `AllowOpaqueOrigin = true`.

## The component's host page

Include the height script (`RemoteComponentFrame.HeightScript`) so the app can size the frame:

```html
<script>
(function () {
    var last = 0;
    function post() {
        var h = Math.ceil(document.documentElement.scrollHeight);
        if (h !== last) { last = h; window.parent.postMessage({ type: 'rbwv-remote-height', height: h }, '*'); }
    }
    if (window.ResizeObserver) new ResizeObserver(post).observe(document.documentElement);
    window.addEventListener('load', post);
})();
</script>
<script src="_framework/blazor.webview.js"></script>
```
