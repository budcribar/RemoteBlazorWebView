using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using PeakSWC.RemoteBlazorWebView.SourceGenerators;

namespace RemoteBlazorWebView.SourceGenerators.Tests;

/// <summary>Runs the generator over test code plus stand-ins for the PeakSWC controls and the bits of WPF/WinForms the generated code
/// uses (so the tests run anywhere, without WPF), then compiles and optionally loads the result.</summary>
internal static class GeneratorHarness
{
    public const string Stubs = """
namespace PeakSWC.RemoteWebView
{
    public class ReadyToConnectEventArgs(System.Guid id, System.Uri url) : System.EventArgs { public System.Guid Id { get; } = id; public System.Uri Url { get; } = url; }
    public class ConnectedEventArgs(System.Guid id, System.Uri url) : System.EventArgs { public System.Guid Id { get; } = id; public System.Uri Url { get; } = url; }
    public class DisconnectedEventArgs : System.EventArgs { }
    public class RefreshedEventArgs : System.EventArgs { }
    public interface IBlazorWebView { void NavigateToString(string html); }
    public class CoreWebView2 { public string? Navigated; public void Navigate(string uri) => Navigated = uri; }
    public class WebView2 { public CoreWebView2? CoreWebView2 { get; } = new(); }
    public abstract class StubView : IBlazorWebView
    {
        public System.Uri? ServerUri { get; set; }
        public System.Guid Id { get; set; } = System.Guid.NewGuid();
        public bool EnableMirrors { get; set; }
        public WebView2 WebView { get; } = new();
        public string? Html;
        public int Restarts;
        public event System.EventHandler<ReadyToConnectEventArgs>? ReadyToConnect;
        public event System.EventHandler<ConnectedEventArgs>? Connected;
        public event System.EventHandler<DisconnectedEventArgs>? Disconnected;
        public event System.EventHandler<RefreshedEventArgs>? Refreshed;
        public void NavigateToString(string html) => Html = html;
        public System.Uri? NavigatedUrl;
        public void NavigateToUrl(System.Uri url) => NavigatedUrl = url;
        public void Restart() => Restarts++;
        public System.Action? RestartWhenUnresponsive { get; set; }
        public void FireReady(System.Uri url, System.Guid id) => ReadyToConnect?.Invoke(this, new(id, url));
        public void FireConnected(System.Uri url, System.Guid id) => Connected?.Invoke(this, new(id, url));
        public void FireDisconnected() => Disconnected?.Invoke(this, new());
        public void FireRefreshed() => Refreshed?.Invoke(this, new());
        public bool HasReady => ReadyToConnect is not null;
        public bool HasConnected => Connected is not null;
        public bool HasDisconnected => Disconnected is not null;
        public bool HasRefreshed => Refreshed is not null;
    }
}
namespace System.Windows
{
    public class Dispatcher { public object? BeginInvoke(System.Delegate method, params object[] args) => method.DynamicInvoke(args); }
    public class Window { public static int Closes; public static Window? GetWindow(object element) => new Window(); public void Close() => Closes++; }
    public class Application { public static int Shutdowns; public static Application? Current { get; } = new(); public void Shutdown() => Shutdowns++; }
}
namespace System.Windows.Forms
{
    public class Form { public static int Closes; public void Close() => Closes++; }
    public static class Application { public static int Exits; public static void Exit() => Exits++; }
}
namespace PeakSWC.RemoteBlazorWebView.Wpf
{
    public class BlazorWebView : PeakSWC.RemoteWebView.StubView { public System.Windows.Dispatcher Dispatcher { get; } = new(); public bool EnableVoice { get; set; } }
}
namespace PeakSWC.RemoteBlazorWebView.WindowsForms
{
    public class BlazorWebView : PeakSWC.RemoteWebView.StubView
    {
        public object? BeginInvoke(System.Delegate method) => method.DynamicInvoke();
        public System.Windows.Forms.Form? FindForm() => new System.Windows.Forms.Form();
    }
}
namespace Microsoft.Web.WebView2.Wpf
{
    public class WebView2 { public System.Uri? Source { get; set; } }
}
namespace Microsoft.AspNetCore.Components.WebView.Wpf
{
    public class BlazorWebView { }
}
""";

    public sealed record Result(ImmutableArray<Diagnostic> GeneratorDiagnostics, ImmutableArray<Diagnostic> CompileErrors, string[] Generated, Assembly? Assembly);

    public static Result Run(string source, string? modeProperty = null, bool load = true)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var parse = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create("Test" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(Stubs, parse, "Stubs.cs"), CSharpSyntaxTree.ParseText(source, parse, "Test.cs")],
            references, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var options = new Options(modeProperty is null ? [] : new Dictionary<string, string> { ["build_property.RemoteBlazorMode"] = modeProperty });
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new RemoteBlazorGenerator().AsSourceGenerator()], optionsProvider: options, parseOptions: parse);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var generatorDiagnostics);
        var generated = driver.GetRunResult().GeneratedTrees.Select(t => t.ToString()).ToArray();

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        var errors = emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        Assembly? assembly = null;
        if (emit.Success && load) assembly = Assembly.Load(stream.ToArray());
        return new Result(generatorDiagnostics, errors, generated, assembly);
    }

    /// <summary>Creates the test class, calls InitializeRemoteBlazor() and returns its result plus the view it set up.</summary>
    public static (bool Remote, dynamic View) Initialize(Assembly assembly, string typeName, string member = "RemoteBlazorWebView", object? startup = null)
    {
        var type = assembly.GetType(typeName, throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;
        var remote = (bool)type.GetMethod("InitializeRemoteBlazor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, [startup])!;
        var view = type.GetField(member, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(instance)
            ?? type.GetProperty(member, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(instance)!;
        return (remote, view);
    }

    private const BindingFlags Internal = BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>What a restart did: the argument list of each copy it started, and how often it ended the process.</summary>
    public sealed class RestartProbe
    {
        public List<string[]> Copies { get; } = [];
        public int Exits;
    }

    /// <summary>Replaces "start a process" and "end this process" in the generated code, so a restart can be observed.</summary>
    public static RestartProbe ProbeRestarts(Assembly assembly)
    {
        var probe = new RestartProbe();
        var type = assembly.GetType("PeakSWC.RemoteBlazorWebView.RemoteBlazorRestart", throwOnError: true)!;
        type.GetProperty("StartProcess", Internal)!.SetValue(null, (Action<System.Diagnostics.ProcessStartInfo>)(info => probe.Copies.Add([.. info.ArgumentList])));
        type.GetProperty("ExitProcess", Internal)!.SetValue(null, (Action)(() => probe.Exits++));
        return probe;
    }

    /// <summary>The command line the generated code reads (after the program name), instead of the test runner's own.</summary>
    public static void CommandLine(Assembly assembly, params string[] args) =>
        assembly.GetType("PeakSWC.RemoteBlazorWebView.RemoteBlazorCommandLine", throwOnError: true)!
            .GetProperty("ArgsOverride", Internal)!.SetValue(null, (string[])["app.exe", .. args]);

    /// <summary>A RemoteBlazorStartup (internal to the test assembly) with the given values.</summary>
    public static object Startup(Assembly assembly, Uri? serverUri = null, Guid? id = null, bool? unattended = null, bool? voice = null,
        Func<Uri, Guid, IEnumerable<string>>? restartArguments = null)
    {
        var type = assembly.GetType("PeakSWC.RemoteBlazorWebView.RemoteBlazorStartup", throwOnError: true)!;
        var startup = Activator.CreateInstance(type, nonPublic: true)!;
        type.GetProperty("ServerUri", Internal)!.SetValue(startup, serverUri);
        type.GetProperty("Id", Internal)!.SetValue(startup, id);
        type.GetProperty("Unattended", Internal)!.SetValue(startup, unattended);
        type.GetProperty("Voice", Internal)!.SetValue(startup, voice);
        type.GetProperty("RestartArguments", Internal)!.SetValue(startup, restartArguments);
        return startup;
    }

    public static int StaticInt(Assembly assembly, string typeName, string field) =>
        (int)assembly.GetType(typeName, throwOnError: true)!.GetField(field)!.GetValue(null)!;

    private sealed class Options(Dictionary<string, string> global) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(global);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Values([]);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values([]);
    }

    private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
