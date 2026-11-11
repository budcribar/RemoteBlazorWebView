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

# Switching between local and remote

Mark the window with `[RemoteBlazor(nameof(MyBlazorWebView), ...)]` and call the generated `InitializeRemoteBlazor()` after `InitializeComponent()`; see [the source generator's README](src/RemoteBlazorWebView.SourceGenerators/README.md).

# Sharing control (owner, remote user, viewers)

A session can be shared between the person at the machine and a remote user, with control handed back and forth.

**Roles.** A session supports exactly these participants:

| Role | How they join | What they can do |
|---|---|---|
| **Owner** (one) | The app's own window shows the controlling page `/app/<id>?role=owner` (`BlazorWebView.NavigateToUrl`). | Starts in control. Can give control to the remote user and can always take it back. |
| **Remote user** (one) | Opens the read-only link `/mirror/<id>` (requires `EnableMirrors`). The remote user is the first non-owner connection in the session. | Watches until the owner gives control; then drives the app and can give control back. Gets the remote user's end of the voice call when voice is on. |
| **Viewers** (any number) | Open the same `/mirror/<id>` link after the remote user. | Read-only. They are never given control, cannot take it, and have no voice. |

Only one connection controls the app at a time; the relay ignores input from everyone else, and control is never given to a second remote user. If the remote user leaves, the next-oldest viewer becomes the remote user. If the connection in control closes, control returns to the owner while the owner is connected; otherwise the session ends.

**Supervised or unattended.** By default a session is supervised: the owner starts in control and decides when to give it. Add `&control=remote` to the owner's page address (`/app/<id>?role=owner&control=remote`) for an unattended session: the remote user is given control as soon as they join, and the owner can still take it back. The app chooses, so its users can pick the policy that suits them.

**Session controls bar (opt-in).** Nothing is drawn on your pages unless you ask. There are three ways to go:

- **No bar.** Leave the placeholder out. The session still works; with `ShareControl` there are then no buttons to hand control over (use an unattended session, or draw your own).
- **The built-in bar.** Put an empty `<div id="rbwv-session-controls" data-share-link="true"></div>` in your layout where the bar should sit. Each browser draws its own bar over it (status, *Give control* / *Take control*, *Copy link* for the owner, voice buttons). `data-restart="true"` adds *Restart app*; `data-host-name` / `data-remote-name` rename the two parties ("The host", "Remote user"), for example `data-remote-name="The technician"`. Restyle with the `rbwv-control-*` / `rbwv-voice-*` classes.
- **Your own bar.** Put a `<template>` inside the placeholder and mark its elements with `data-rbwv`. Each browser gets a copy with only the parts that apply to it; the status is filled in and the buttons are wired. Layout, wording, classes and icons are yours:

  ```html
  <div id="rbwv-session-controls">
      <template>
          <span data-rbwv="dot"></span>
          <strong data-rbwv="status" data-text-you="You are driving"></strong>
          <button class="my-btn" data-rbwv="give">Hand over</button>
          <button class="my-btn" data-rbwv="take">Take back</button>
          <button class="my-btn" data-rbwv="copy-link" data-copied-text="Copied!">Invite</button>
      </template>
  </div>
  ```

  Parts: `dot`, `status`, `give`, `take`, `copy-link`, `restart` (with optional `restart-question`, `restart-confirm`, `restart-cancel` for a confirmation step), `voice-status`, `voice-act`, `voice-mute`, `voice-end`. On the status element `data-text-you`, `data-text-view-only`, `data-text-none`, `data-text-switching` and `data-text-restarting` replace the wording. A part left empty gets the built-in wording.

Put the placeholder in your layout, not on one page, or the bar goes away when the user navigates. It only shows when there is something to control: a session with an owner, or a voice call. A dot is lit while anyone is connected remotely; hovering over it says how many. For anything the template cannot do, `window.RemoteControl` (and `window.RemoteVoice`) expose the same state and actions to your own script; draw that in JavaScript above the page (not as a Blazor component: the app renders once for every browser, and clicks from whoever does not have control are dropped).

**Restart button.** Add `data-restart="true"` to the placeholder to give whoever has control (and the owner, always) a *Restart app* button, with a confirm step. The relay sends the app the same `Refreshed` event a browser refresh raises, so an app that restarts itself there needs nothing else; every browser page then waits for the new copy and loads again by itself. The button never appears without the placeholder.

**With the source generator.** `[RemoteBlazor(nameof(RemoteBlazorWebView), ShareControl = true)]` (or `Unattended = true`) generates this setup; see the generator's README.

**Upgrade the relay and the packages together.** Sharing control needs relay and packages 10.0.15 or later on both sides. With an older relay the *Give control* / *Take control* buttons do nothing. An app on older packages keeps working against a 10.0.15 relay.

**Remote components.** A remote component's frame follows the page it is on: it is usable in the browser that controls the app and read-only in the others, and moves with control. Viewers always see it read-only.

# Sign-in on the relay

By default the relay asks nobody to sign in: a session's pages open for anyone who has the link. The
`Authorization:Provider` setting (environment variable `Authorization__Provider`) chooses otherwise:

| Provider | What it does |
|---|---|
| `None` (default) | No sign-in. |
| `AzureB2C` | Azure AD B2C sign-in (settings: the `AzureAdB2C` section and `Secret`). In every relay build except the native one (`-c NoAuthorization`). A relay built with `-c Authorization` uses it without the setting. |
| `Fake` | For tests: the request names its user as `name;group1,group2` in the `X-Rbwv-Fake-User` header, the `fake-user` query value or the `rbwv-fake-user` cookie. The relay refuses to start with it outside the Development environment. |

With sign-in on, a session's pages (`/app`, `/mirror`, `/<id>`, `/status`, `/wait`) open for a signed-in user whose groups
include the session's `Group`, and for the app's own window, which needs no account: it opens its page with the owner key
(below). Each connection's signed-in name is in the roster. A relay hosted in-process takes a provider directly:
`RemoteWebViewServerOptions.Authorization`. A provider is an `IRelayAuthorization`.

**Owner key.** With `ShareControl`, the app makes a secret for the session (`OwnerKey`) and opens its own page as
`/app/<id>?role=owner&key=<key>`. Only that page is the owner; `?role=owner` added to any other link is ignored.
`ParticipantsChanged` on the control reports who is in the session, their names and who has control.

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

