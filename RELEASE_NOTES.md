# Release notes

## 10.0.15.2

Upgrade the relay (RemoteWebViewService) and the packages together. Pages from 10.0.15.1 and older keep working on a
10.0.15.2 relay, but they don't get the new protection against a viewer taking control.

### Packages

- **A page's requests carry a secret key.** Before, every browser in a session was sent every other browser's id, and the
  relay checked only the id, so a viewer could take control, send input or restart the app by putting the controller's or
  the owner's id in its own request. Each page now picks a key and sends it with its requests, and the relay never passes it
  on. A remote component's frame takes its key from its parent page. Pages from older packages send no key and work as
  before.
- **`window.RemoteControl` reports the connection.** `getState().connectionState` is `connecting` until the first message
  arrives, then `connected`. It becomes `reconnecting` while a lost or restarting session is waited for, and
  `disconnected` once the page gives up. An app that shows control from `iHaveControl` should not treat a page as in
  control unless this is `connected`: after a lost connection, the page keeps the control it last had.
- Voice with no session bar placeholder warns in the browser console instead of silently showing no call buttons.

### Relay (RemoteWebViewService and RemoteWebViewService.Lib)

- **Agents are refused when `AGENT_ENROLLMENT_KEYS` is unset (behaviour change).** Outside Development, a relay with no
  enrollment keys used to accept any agent. It now refuses all of them. **Set `AGENT_ENROLLMENT_KEYS` before deploying**,
  or every agent is locked out. The `RemoteTicket` provider is unchanged.
- **Shared sessions send non-owner browsers to the mirror.** In a session whose app sent an owner key, a browser asking
  for `/app/<id>` or `/<id>` without the owner key or cookie is redirected to `/mirror/<id>`. A remote tab coming back after
  the PC rebooted can no longer restart the app. A mirror reached before the app's own page is open gets a 503 page that
  reloads itself every 3 seconds.
- The owner's own window is never locked out or taken for a refresh when another browser reaches a restarted app first.
- A browser that leaves straight after joining is removed from the session. Before, it stayed in the roster for the rest
  of the session.
- `/status/<id>` no longer needs sign-in, and it reports only a finished registration. Agent-started sessions on a relay
  with sign-in no longer wait an extra 20 seconds.
- **Signed RemoteTicket authorization** (`RemoteWebViewService.RemoteTicket`), with optional `IRelayEndpointExtension`,
  `IShopSessionPolicy` and `IAgentTenantPolicy` hooks. The ticket can also come from the query string, and the relay then
  sets its auth cookie.
- Agent admin API: `EndSession` ends a computer's newest agent-started session, `PublishRelease` broadcasts a
  `ReleaseAvailableCommand` to agents, `GetRequester` labels signed-in callers, and a new `STAGE_CONSENT_TIMED_OUT` launch
  stage ends the watch when a consent prompt goes unanswered. Deploy the relay before agents that send the new stage.
- Every session shutdown is logged once, at Warning, with its reason (`SessionEndReason`). Page requests on `/app`, `/<id>`
  and `/mirror` log their decision at Information. The query, which can hold the owner key, is never logged.

## 10.0.15.1

Packages only; the relay does not change.

- **The session controls bar is opt-in (behaviour change).** Nothing is drawn on an app's pages unless its layout has the
  `<div id="rbwv-session-controls">` placeholder. Before, an app that shared control or used voice without a placeholder got
  a bar floating bottom-right; that bar is gone (it also flashed at startup in apps that did have the placeholder). **If your
  app relied on the floating bar, add the placeholder to your layout**, or the *Give control* / *Take control* and voice
  buttons are no longer shown.
- With the placeholder, the bar shows only when there is something to control: a session with an owner, or a voice call.
- **Your own bar from a template.** Put a `<template>` inside the placeholder and mark its elements with `data-rbwv`
  (`status`, `give`, `take`, `copy-link`, `restart`, the voice parts, ...). Each browser gets a copy with only the parts that
  apply to it, the status filled in and the buttons wired; layout, wording and styling are the app's. See "Session controls
  bar" in the README. `window.RemoteControl` remains for a bar drawn entirely by the app.
- Fixed: after *Restart app*, a browser page that had been opened as the owner (`?role=owner`) and had moved to another
  page of the app came back as an ordinary page, no longer the owner. It now comes back with the query it was opened with.

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
