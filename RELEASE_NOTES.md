# Release notes

## 10.0.15

Upgrade the relay (RemoteWebViewService) and the packages together: the new features need 10.0.15 on both sides.

### New

- **Sharing control.** The person at the machine (the owner) and a remote user share one session and hand control back and
  forth. The owner's window shows the app itself (`/app/<id>?role=owner`); the remote user joins at the read-only `/mirror/<id>`
  link. One owner, one remote user (the first to join), any number of read-only viewers. See "Sharing control" in the README.
- **Unattended sessions.** `&control=remote` on the owner's page gives the remote user control as soon as they join.
- **Session controls bar.** Each browser draws its own status, *Give control* / *Take control*, *Copy link* and voice buttons,
  in `<div id="rbwv-session-controls">` if the app has one, otherwise floating. A dot shows when anyone is connected remotely.
  The wording is neutral ("The host", "Remote user"); `data-host-name` / `data-remote-name` rename the two parties. `window.RemoteControl` exposes the state for a custom UI.
- **Voice call** between the owner and the remote user (`EnableVoice`, https relay).
- **`BlazorWebView.NavigateToUrl(Uri)`** (WPF and WinForms): load a page in the control's own WebView once it is ready.
- **Source generator:** `[RemoteBlazor(..., ShareControl = true)]` and `Unattended = true`; diagnostic RBW011.
- **Remote components** follow control: usable in the browser that controls the app, read-only in the others.
- **Relay version** (with the commit) in `GetServerStatus`, shown by the admin site.

### Fixed

- Host crash when control changed hands while a render was in flight (duplicate or missing render acknowledgements).
- Host crash when a `/mirror` page in control sent the connect handshake.
- Trimmed / native (AOT) relay: voice messages, the participants list, the status endpoint and settings no longer rely on reflection.
- Build warnings: MSB3277 (WebView2 WPF assembly in non-WPF projects), RS1032.

### Behaviour changes

- `/app/<id>?role=owner`, and the `/<id>/...` app page it redirects to, now load inside the control's WebView. A plain `/app/<id>`
  link still opens in the system browser, as before.
- The relay keeps the query string when `/app/<id>` redirects to `/<id>`.
- The relay lists participants in the order they joined.

### Compatibility

- Apps that use `Mirror`, `ReverseMirror` or neither are unchanged, and see no session bar unless they add the placeholder,
  an owner page or voice.
- 10.0.14 packages work against a 10.0.15 relay. 10.0.15 packages against a 10.0.14 relay work as before, but cannot share control.
