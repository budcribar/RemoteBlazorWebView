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

# Sharing control (owner, technician, viewers)

A session can be shared between the person at the machine and a remote technician, with control handed back and forth.

**Roles.** A session supports exactly these participants:

| Role | How they join | What they can do |
|---|---|---|
| **Owner** (one) | The app's own window shows the controlling page `/app/<id>?role=owner` (`BlazorWebView.NavigateToUrl`). | Starts in control. Can give control to the technician and can always take it back. |
| **Technician** (one) | Opens the read-only link `/mirror/<id>` (requires `EnableMirrors`). The technician is the first non-owner connection in the session. | Watches until the owner gives control; then drives the app and can give control back. Gets the technician's end of the voice call when voice is on. |
| **Viewers** (any number) | Open the same `/mirror/<id>` link after the technician. | Read-only. They are never given control, cannot take it, and have no voice. |

Only one connection controls the app at a time; the relay ignores input from everyone else, and control is never given to a second technician. If the technician leaves, the next-oldest viewer becomes the technician. If the connection in control closes, control returns to the owner while the owner is connected; otherwise the session ends.

**Supervised or unattended.** By default a session is supervised: the owner starts in control and decides when to give it. Add `&control=technician` to the owner's page address (`/app/<id>?role=owner&control=technician`) for an unattended session: the technician is given control as soon as they join, and the owner can still take it back. The app chooses, so its users can pick the policy that suits them.

**Session controls bar.** Each browser draws its own bar (status, *Give control* / *Take control*, *Copy link* for the owner, voice buttons). Put an empty `<div id="rbwv-session-controls" data-share-link="true"></div>` where the bar should sit in your layout; without it the bar floats bottom-right. A floating bar only appears when the session has an owner or a voice call, so an app that uses neither looks as it did before. Restyle with the `rbwv-control-*` / `rbwv-voice-*` classes. `window.RemoteControl` exposes the same state for a custom UI.

**With the source generator.** `[RemoteBlazor(nameof(RemoteBlazorWebView), ShareControl = true)]` (or `Unattended = true`) generates this setup; see the generator's README.

**Upgrade the relay and the packages together.** Sharing control needs relay and packages 10.0.15 or later on both sides. With an older relay the *Give control* / *Take control* buttons do nothing. An app on older packages keeps working against a 10.0.15 relay.

**Remote components.** A remote component's frame follows the page it is on: it is usable in the browser that controls the app and read-only in the others, and moves with control. Viewers always see it read-only.

# Samples

Check out the tutorial at https://github.com/budcribar/RemoteBlazorWebViewTutorial 

