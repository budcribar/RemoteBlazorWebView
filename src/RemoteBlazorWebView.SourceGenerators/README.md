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
| `RestartOnRefresh` | `false` | On browser refresh: `Restart()` (a new copy with the same `-u=`/`-i=`) and close the window |
| `ShutdownOnDisconnect` | `false` | On disconnect: `Application.Current.Shutdown()` (WPF) or `Application.Exit()` (WinForms) |

The `RemoteBlazorMode` MSBuild property overrides `Mode` for a build: `dotnet build -p:RemoteBlazorMode=Local`, or
`<RemoteBlazorMode>Local</RemoteBlazorMode>` in the project.

What the generator can't do: swap the `Microsoft.AspNetCore.Components.WebView.Wpf` package and the XAML namespace for the PeakSWC
ones. That's a one-time edit; the PeakSWC control behaves like Microsoft's when it has no `ServerUri`, so you never switch back.

Diagnostics: RBW001 member not found (warning), RBW002 member isn't a PeakSWC BlazorWebView, RBW003 class isn't partial,
RBW004 `Remote` without `ServerUri`, RBW005 bad `ServerUri`, RBW006 bad `Id`, RBW007 unknown `RemoteBlazorMode` value (warning),
RBW008 `ServerUri` set in `Runtime` mode, where it isn't used (warning).

When you reference the RemoteBlazorWebView projects instead of the packages, add the generator and the props yourself:

```xml
<ProjectReference Include="..\RemoteBlazorWebView\src\RemoteBlazorWebView.SourceGenerators\RemoteBlazorWebView.SourceGenerators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<Import Project="..\RemoteBlazorWebView\src\RemoteWebView\buildTransitive\PeakSWC.RemoteWebView.props" />
```
