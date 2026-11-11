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
- **Restart button** (opt-in: `data-restart="true"` on the placeholder). Whoever has control, and the owner, can restart the app from the session bar. The app receives the existing `Refreshed` event; browser pages wait for the new copy and come back by themselves.
- Fixes found in a review of shared control: a message from before a hand-off could later replace a real one; a browser joining could stop all input reaching the app; the remote user lost voice by refreshing; two hand-offs at once could leave browsers and relay disagreeing.
- **A lost connection is shown.** A browser page whose connection to the session drops says so instead of staying on screen with nothing working. A `/mirror` page waits for the session and loads again by itself, rebuilt to the current state; a remote user who had control is then a viewer until given control again (at once in an unattended session).
- **Owner key.** Only the app's own window is the owner: the app sends the relay a secret (`OwnerKey`, set by the source generator for `ShareControl`) and opens `/app/<id>?role=owner&key=...`. Before, anyone could add `?role=owner` to a session link and take control. Apps on older packages keep the old behaviour.
- **`ParticipantsChanged`** on the controls: who is in the session, their signed-in names, who has control (for an audit trail).
- **Sign-in is a setting, not a build.** `Authorization:Provider` = `None` (default), `AzureB2C` or `Fake` (tests, Development only); `IRelayAuthorization`. With sign-in on, a session's pages open only for users in the session's group, and for the app's own window without an account. See "Sign-in on the relay" in the README.
- **Voice call** between the owner and the remote user (`EnableVoice`, https relay).
- **`BlazorWebView.NavigateToUrl(Uri)`** (WPF and WinForms): load a page in the control's own WebView once it is ready.
- **Source generator:** `[RemoteBlazor(..., ShareControl = true)]` and `Unattended = true`; diagnostic RBW011. A `Voice` option (RBW012 on WinForms); `--unattended` and `--voice` at run time; `InitializeRemoteBlazor(new RemoteBlazorStartup { ... })` for apps that work their settings out themselves; `RestartOnRefresh` now gives the new copy this run's own command line and still restarts an app whose window is not responding.
- **`RestartWhenUnresponsive`** on the controls: called on a background thread when a restart was requested but the UI thread did not respond within 5 seconds.
- **Remote components** follow control: usable in the browser that controls the app, read-only in the others.
- **Relay version** (with the commit) in `GetServerStatus`, shown by the admin site.

### Fixed

- Host crash when control changed hands while a render was in flight (duplicate or missing render acknowledgements).
- Host crash when a `/mirror` page in control sent the connect handshake.
- Trimmed / native (AOT) relay: voice messages, the participants list, the status endpoint and settings no longer rely on reflection.
- Build warnings: MSB3277 (WebView2 WPF assembly in non-WPF projects), RS1032.

- Unattended sessions: the automatic hand-over of control is repeated when the first request does not take.
- Remote components could be left following the previous controller after two quick changes of control.

### Known issues

- A drop on the app's side (the machine loses its connection to the relay, or the relay restarts) ends the session. The pages say so; the app has to be started again to carry on under the same link.
- The owner's own page (the app's window in a shared session) does not come back by itself after a lost connection; if it held control, the session ends.
- A JavaScript interop call that is waiting for its answer at the moment control changes hands does not complete.

### Behaviour changes

- `/app/<id>?role=owner`, and the `/<id>/...` app page it redirects to, now load inside the control's WebView. A plain `/app/<id>`
  link still opens in the system browser, as before.
- The relay keeps the query string when `/app/<id>` redirects to `/<id>`.
- The relay lists participants in the order they joined.

### Compatibility

- Apps that use `Mirror`, `ReverseMirror` or neither are unchanged, and see no session bar unless they add the placeholder,
  an owner page or voice.
- 10.0.14 packages work against a 10.0.15 relay. 10.0.15 packages against a 10.0.14 relay work as before, but cannot share control.
