# Remote components

Run a Blazor component in its own process and show it inside your app, in a sandboxed iframe. Use it when a piece of UI should be
isolated from the app: it can run with fewer rights than the app, crash without taking the app down, and can't touch the app's page.

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
  contract values that changed, the component sends `call` for a callback. Nothing crosses through the browser, so when the app
  is viewed remotely, the remote browser can't forge either side.
- **The iframe** is sandboxed without `allow-same-origin` (`RemoteComponentFrame.DefaultSandbox`), so it has an opaque origin. The
  only `postMessage` the app's page accepts from it is its height (`rbwv-remote-height`), matched by the sending window.

## Mirrors

When the app's page is mirrored (a read-only `/mirror/<id>` view), the mirror renders the same page, frame included. If the mirror
loaded the component's page as well, the relay would take that as a reload and the component's process would restart. So the frame
is rendered without `src`: `data-rbwv-src` holds the component's page, and `data-rbwv-mirror-src` the relay's read-only view of it
(`RemoteComponentSession.MirrorSource`, by default `<relay>/mirror/<id>`). The app's page loads the first (`remotecomponents.js`); a
mirror page loads the second (the RemoteWebView script), or nothing when the session has no mirror source. The component's process
allows mirrors by default (`RemoteComponentHostOptions.EnableMirrors`). Mirrors can't click or type in it, like the rest of the mirror.

## A contract, and what the generator writes from it

Declare what crosses between the two processes as an interface, in a library both sides reference:

```csharp
[RemoteComponentContract]
public interface IClock
{
    string TimeZone { get; }              // a value: app -> component
    Alarm? NextAlarm { get; }             // any JSON-serializable type
    Task Snooze();                         // a callback: component -> app
    Task Picked(DateTimeOffset when);      // at most one argument
}
```

The source generator in this package writes:

- **`ClockFrame`** for the app, a component with `[Parameter] TimeZone`, `NextAlarm`, `EventCallback Snooze` and
  `EventCallback<DateTimeOffset> Picked`:

  ```razor
  <ClockFrame Open="ct => launcher.StartAsync(ct)" TimeZone="@zone" NextAlarm="@alarm" Snooze="OnSnooze" Title="Clock" />
  ```

- **`ClockReceiver`** for the component's process: implements `IClock` with the values the app sent (`Changed` fires when they
  change); calling `Snooze()` sends the callback. Register it in the host and inject `IClock` (or `ClockReceiver`) in the UI:

  ```csharp
  return RemoteComponentHost.Run(new RemoteComponentHostOptions
  {
      ServerUri = relay, GrpcBaseUri = grpc, Id = id, ParentProcessId = appPid, RootComponent = typeof(ClockRoot),
      ConfigureServices = (services, connection) => services.AddRemoteComponentReceiver<ClockReceiver>(connection),
  });
  ```

Values and arguments must be JSON-serializable (`RemoteComponentJson.Options`: web defaults, enums as strings). Values are sent
when they change (compared as JSON), all of them the first time. Rules the generator enforces: RBWRC001 for a member that isn't a
get-only property or a `Task` method with at most one argument, RBWRC002 for a generic or nested interface, RBWRC003 for a member name
already defined on `RemoteComponentFrameBase` or `RemoteComponentReceiverBase`. Names can be set with
`[RemoteComponentContract(FrameName = "...", ReceiverName = "...")]`.

Razor compiles before source generators run, so a `.razor` file can't use a frame generated in its own project: keep the contract
in a separate library.

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
