# [RemoteBlazor] source generator

Ships inside `PeakSWC.RemoteWebView` (so every app that references `PeakSWC.RemoteBlazorWebView.Wpf` or `.WindowsForms` gets it).
It writes the code-behind that the tutorial's
[ConvertToRemoteBlazor.md](https://github.com/budcribar/RemoteBlazorWebViewTutorial/blob/net10/WpfBlazor/ConvertToRemoteBlazor.md)
otherwise has you type by hand.

```csharp
using PeakSWC.RemoteBlazorWebView;

[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001",
    RestartOnRefresh = true, ShutdownOnDisconnect = true)]
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var services = new ServiceCollection();
        services.AddRemoteWpfBlazorWebView();
        Resources.Add("services", services.BuildServiceProvider());
        InitializeRemoteBlazor(); // generated; returns true when the view is remote
    }
}
```

| Attribute property | Default | What the generated code does |
|---|---|---|
| `WebView` (constructor) | | The field or property holding the PeakSWC `BlazorWebView` (the XAML `x:Name`, or the WinForms designer field) |
| `Mode` | `Runtime` | `Runtime`: remote only when started with `-u=<server uri>`. `Remote`: always remote, on `-u=` or else `ServerUri`. `Local`: does nothing |
| `ServerUri` | | The RemoteWebViewService for `Remote` mode |
| `Id` | new id each run | A fixed client id (Guid); `-i=<guid>` overrides it |
| `ShowLink` | `true` | Shows `<server>/app/<id>` as a link in the view until a browser connects |
| `Mirror` | `false` | `EnableMirrors`, and shows the read-only mirror in the view once a browser connects |
| `ReverseMirror` | | Name of a WebView2 you put over the BlazorWebView. `EnableMirrors`; the WebView2 opens `<server>/app/<id>`, so the person at the PC uses the app through the server and it looks local; once connected, the system browser opens the read-only `<server>/mirror/<id>` to share. Replaces `ShowLink`; not with `Mirror` |
| `ShareControl` | `false` | Shared control: `EnableMirrors`, and this window shows the app itself (`<server>/app/<id>?role=owner`) with the person at the PC in control. The browser joins at the read-only `<server>/mirror/<id>` (*Copy link* in the session bar) and is handed control from the bar; the owner can always take it back. Replaces `ShowLink`; not with `Mirror` or `ReverseMirror` |
| `Unattended` | `false` | `ShareControl`, and the technician is given control as soon as they join (the owner can still take it back) |
| `RestartOnRefresh` | `false` | On browser refresh: `Restart()` (a new copy with the same `-u=`/`-i=`) and close the window |
| `ShutdownOnDisconnect` | `false` | On disconnect: `Application.Current.Shutdown()` (WPF) or `Application.Exit()` (WinForms) |

The `RemoteBlazorMode` MSBuild property overrides `Mode` for a build: `dotnet build -p:RemoteBlazorMode=Local`, or
`<RemoteBlazorMode>Local</RemoteBlazorMode>` in the project.

What the generator can't do: swap the `Microsoft.AspNetCore.Components.WebView.Wpf` package and the XAML namespace for the PeakSWC
ones. That's a one-time edit; the PeakSWC control behaves like Microsoft's when it has no `ServerUri`, so you never switch back.

Diagnostics: RBW001 member not found (warning), RBW002 member isn't a PeakSWC BlazorWebView, RBW003 class isn't partial,
RBW004 `Remote` without `ServerUri`, RBW005 bad `ServerUri`, RBW006 bad `Id`, RBW007 unknown `RemoteBlazorMode` value (warning),
RBW008 `ServerUri` set in `Runtime` mode, where it isn't used (warning), RBW009 `Mirror` and `ReverseMirror` together,
RBW010 `ReverseMirror` isn't a WebView2, RBW011 `ShareControl` or `Unattended` with `Mirror` or `ReverseMirror`.

## Shared control

The person at the PC and a remote technician use the same app, and control is handed back and forth. Nothing to add to the XAML:

```csharp
[RemoteBlazor(nameof(RemoteBlazorWebView), ShareControl = true)]   // or Unattended = true
public partial class MainWindow : Window
```

Each browser draws a small session bar (who has control, *Give control* / *Take control*, *Copy link*). Put
`<div id="rbwv-session-controls" data-share-link="true"></div>` in your layout where it should sit; without it the bar floats
bottom-right. A session has one owner (this window), one technician (the first to open the link) and any number of read-only viewers.
See "Sharing control" in the repository README.

## Reverse mirror

The person at the PC keeps using the app in its own window, and a browser elsewhere gets a read-only view. The generator can't
add the WebView2 control, so add it to the XAML yourself, over the BlazorWebView:

```xml
xmlns:wv2="clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf"
...
<Grid>
    <blazor:BlazorWebView x:Name="RemoteBlazorWebView" ... />
    <wv2:WebView2 x:Name="WebView" />
</Grid>
```

```csharp
[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001",
    ReverseMirror = nameof(WebView))]
```

WinForms works the same with a `Microsoft.Web.WebView2.WinForms.WebView2` docked over the BlazorWebView.

When you reference the RemoteBlazorWebView projects instead of the packages, add the generator and the props yourself:

```xml
<ProjectReference Include="..\RemoteBlazorWebView\src\RemoteBlazorWebView.SourceGenerators\RemoteBlazorWebView.SourceGenerators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<Import Project="..\RemoteBlazorWebView\src\RemoteWebView\buildTransitive\PeakSWC.RemoteWebView.props" />
```
