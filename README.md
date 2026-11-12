# RemoteBlazorWebView.Wpf.BlazorWebView

RemoteBlazorWebView.Wpf.BlazorWebView is a powerful control based on the .NET 9 Blazor WebView Control for WPF applications. It allows you to interact with the user interface of a program developed using the BlazorWebView WPF control through a web browser by leveraging a cloud-based server.

The RemoteBlazorWebView.Wpf.BlazorWebView control facilitates remote interaction with your application's user interface by setting up a server (RemoteWebViewService) in the cloud and connecting your browser to it.

As a drop-in replacement for the Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView control, RemoteBlazorWebView.Wpf requires only minimal changes to your existing application to enable remote control capabilities. This makes it an efficient and convenient solution for extending your application's functionality.


# RemoteBlazorWebView.WindowsForms.BlazorWebView

RemoteBlazorWebView.WindowsForms.BlazorWebView is a robust control built on the .NET 9 Blazor WebView WinForms Control, designed for Windows Forms applications. It allows you to engage with the user interface of a program created using the BlazorWebView WinForms control through a web browser, utilizing a cloud-based server.

The RemoteBlazorWebView.WindowsForms.BlazorWebView control enables remote interaction with your application's user interface by establishing a server (RemoteWebViewService) in the cloud and directing your browser to connect with it.

As a seamless replacement for the Microsoft.AspNetCore.Components.WebView.WindowsForms.BlazorWebView control, RemoteBlazorWebView.WindowsForms.BlazorWebView necessitates only minimal adjustments to your current application to empower remote control capabilities. This makes it an effective and user-friendly option for enhancing your application's features.

# Demo Video
![RemoteBlazorWebView](https://raw.githubusercontent.com/budcribar/RemoteBlazorWebView/net10/icons/RemoteBlazorWebView.gif)

# Usage instructions

You do not need to build this repo unless you want to customize the RemoteWebViewService. Run the following command to install the RemoteWebViewService

```console
dotnet tool update -g PeakSWC.RemoteWebViewService --version 9.*-* 
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

# Fast tests

The FileSync desktop and tutorial Playwright classes are marked `[Trait("Category", "Slow")]`. A normal `dotnet test` still runs them. To skip only that suite:

```console
dotnet test --filter Category!=Slow
dotnet build src/RemoteWebViewService/RemoteWebViewService.csproj -c NoAuthorization
```

`Category!=Slow` keeps Shop Identity, the relay and embedded-server tests, agent registry and hub tests, and the FileSync headless classes (`FileFetchingTests`, `ClientCachingTests`, `ServerCachingTests`, `ConcurrentRequestsTests`, `LoadTest`, `PerformanceTests`, `TestMisc`). On Windows, `.\RunTests.ps1 -Fast` does the same and checks that the NoAuthorization build does not reference Shop Identity or SQLite.

Pushes to `net10` still run the full Windows desktop and tutorial step and the performance regression workflow. An on-demand run is `.github/workflows/fast-tests.yml` (`workflow_dispatch` only). 

