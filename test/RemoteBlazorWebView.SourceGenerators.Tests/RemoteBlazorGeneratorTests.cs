namespace RemoteBlazorWebView.SourceGenerators.Tests;

public class RemoteBlazorGeneratorTests
{
    private static string Wpf(string attribute, string member = "internal PeakSWC.RemoteBlazorWebView.Wpf.BlazorWebView RemoteBlazorWebView = new();") => $$"""
using PeakSWC.RemoteBlazorWebView;
namespace App
{
    {{attribute}}
    public partial class MainWindow : System.Windows.Window
    {
        {{member}}
    }
}
""";

    private static void NoErrors(GeneratorHarness.Result r)
    {
        Assert.Empty(r.GeneratorDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(r.CompileErrors);
    }

    [Fact]
    public void Remote_mode_sets_server_id_and_link()
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001")]"""));
        NoErrors(r);
        Assert.Empty(r.GeneratorDiagnostics);
        var (remote, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");
        Assert.True(remote);
        Assert.Equal(new Uri("https://localhost:5001"), (Uri)view.ServerUri);
        Assert.Equal(Guid.Empty, (Guid)view.Id);
        Assert.False((bool)view.EnableMirrors);
        Assert.True((bool)view.HasReady);
        Assert.False((bool)view.HasConnected);
        Assert.False((bool)view.HasRefreshed);
        Assert.False((bool)view.HasDisconnected);

        var id = Guid.NewGuid();
        view.FireReady(new Uri("https://localhost:5001/"), id);
        Assert.Equal($"<a href='https://localhost:5001/app/{id}' target='_blank'>https://localhost:5001/app/{id}</a>", (string)view.Html);
    }

    [Fact]
    public void Runtime_mode_is_local_without_dash_u()
    {
        var r = GeneratorHarness.Run(Wpf("[RemoteBlazor(nameof(RemoteBlazorWebView))]"));
        NoErrors(r);
        Assert.Empty(r.GeneratorDiagnostics);
        var (remote, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");
        Assert.False(remote);
        Assert.Null((Uri?)view.ServerUri);
        Assert.False((bool)view.HasReady);
    }

    [Fact]
    public void Local_mode_does_nothing()
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Local, ServerUri = "https://localhost:5001", ShutdownOnDisconnect = true)]"""));
        NoErrors(r);
        var (remote, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");
        Assert.False(remote);
        Assert.Null((Uri?)view.ServerUri);
        Assert.False((bool)view.HasDisconnected);
    }

    [Theory]
    [InlineData("Local", false)]
    [InlineData("remote", true)]
    public void MSBuild_property_overrides_the_attribute(string property, bool expectRemote)
    {
        var attribute = expectRemote
            ? """[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Local, ServerUri = "https://localhost:5001")]"""
            : """[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001")]""";
        var r = GeneratorHarness.Run(Wpf(attribute), modeProperty: property);
        NoErrors(r);
        var (remote, _) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");
        Assert.Equal(expectRemote, remote);
    }

    [Fact]
    public void Unknown_MSBuild_mode_warns_and_uses_the_attribute()
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001")]"""), modeProperty: "Cloud");
        NoErrors(r);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW007");
        Assert.True(GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow").Remote);
    }

    [Fact]
    public void Advanced_options_wire_mirror_refresh_and_disconnect_on_wpf()
    {
        var id = "6d1a1b8e-0f53-4f39-9c55-0f7b7c4b8a10";
        var r = GeneratorHarness.Run(Wpf($$"""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", Id = "{{id}}", ShowLink = false, Mirror = true, RestartOnRefresh = true, ShutdownOnDisconnect = true)]"""));
        NoErrors(r);
        GeneratorHarness.CommandLine(r.Assembly!);
        var probe = GeneratorHarness.ProbeRestarts(r.Assembly!);
        var (remote, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");
        Assert.True(remote);
        Assert.Equal(Guid.Parse(id), (Guid)view.Id);
        Assert.True((bool)view.EnableMirrors);
        view.FireReady(new Uri("https://localhost:5001/"), Guid.Parse(id));
        Assert.Null((string?)view.Html); // ShowLink = false: no link page (the restart only notes the session id here)

        view.FireConnected(new Uri("https://localhost:5001/"), Guid.Parse(id));
        Assert.Equal($"https://localhost:5001/mirror/{id}", (string)view.WebView.CoreWebView2.Navigated);

        view.FireRefreshed();
        Assert.Equal(["-u=https://localhost:5001/", $"-i={id}"], Assert.Single(probe.Copies)); // a fresh copy on the same server and id
        Assert.Equal(0, probe.Exits); // the window closes normally
        Assert.Equal(1, GeneratorHarness.StaticInt(r.Assembly!, "System.Windows.Window", "Closes"));

        view.FireDisconnected();
        Assert.Equal(1, GeneratorHarness.StaticInt(r.Assembly!, "System.Windows.Application", "Shutdowns"));
    }

    [Fact]
    public void WinForms_uses_form_close_and_application_exit()
    {
        var r = GeneratorHarness.Run("""
using PeakSWC.RemoteBlazorWebView;
namespace App
{
    [RemoteBlazor("blazorWebView1", Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", RestartOnRefresh = true, ShutdownOnDisconnect = true)]
    public partial class Form1 : System.Windows.Forms.Form
    {
        private PeakSWC.RemoteBlazorWebView.WindowsForms.BlazorWebView blazorWebView1 = new();
    }
}
""");
        NoErrors(r);
        GeneratorHarness.CommandLine(r.Assembly!);
        var probe = GeneratorHarness.ProbeRestarts(r.Assembly!);
        var (remote, view) = GeneratorHarness.Initialize(r.Assembly!, "App.Form1", "blazorWebView1");
        Assert.True(remote);
        view.FireRefreshed();
        Assert.Single(probe.Copies);
        Assert.Equal(1, GeneratorHarness.StaticInt(r.Assembly!, "System.Windows.Forms.Form", "Closes"));
        view.FireDisconnected();
        Assert.Equal(1, GeneratorHarness.StaticInt(r.Assembly!, "System.Windows.Forms.Application", "Exits"));
    }

    [Fact]
    public void Nested_class_in_global_namespace_and_property_member()
    {
        var r = GeneratorHarness.Run("""
using PeakSWC.RemoteBlazorWebView;
public partial class Outer
{
    [RemoteBlazor(nameof(View), Mode = RemoteBlazorMode.Remote, ServerUri = "https://example.com/")]
    public partial class Inner
    {
        public PeakSWC.RemoteBlazorWebView.Wpf.BlazorWebView View { get; } = new();
    }
}
""");
        NoErrors(r);
        var (remote, view) = GeneratorHarness.Initialize(r.Assembly!, "Outer+Inner", "View");
        Assert.True(remote);
        Assert.Equal(new Uri("https://example.com/"), (Uri)view.ServerUri);
    }

    [Fact]
    public void Missing_member_warns_and_generates_a_no_op()
    {
        // What the WPF markup compiler's first pass looks like: the x:Name field doesn't exist yet.
        var r = GeneratorHarness.Run(Wpf("[RemoteBlazor(nameof(RemoteBlazorWebView))]", member: ""), load: false);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW001");
        // nameof() of a missing member is the only compile error; with a string literal it compiles.
        var r2 = GeneratorHarness.Run(Wpf("""[RemoteBlazor("RemoteBlazorWebView")]""", member: "public void Use() => InitializeRemoteBlazor();"));
        Assert.Contains(r2.GeneratorDiagnostics, d => d.Id == "RBW001");
        Assert.Empty(r2.CompileErrors);
    }

    [Fact]
    public void Microsoft_BlazorWebView_is_an_error()
    {
        var r = GeneratorHarness.Run(Wpf("[RemoteBlazor(nameof(RemoteBlazorWebView))]",
            member: "internal Microsoft.AspNetCore.Components.WebView.Wpf.BlazorWebView RemoteBlazorWebView = new();"), load: false);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW002" && d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Single(r.Generated); // just the attribute
    }

    [Fact]
    public void Non_partial_class_is_an_error()
    {
        var r = GeneratorHarness.Run("""
using PeakSWC.RemoteBlazorWebView;
[RemoteBlazor(nameof(V))]
public class MainWindow { internal PeakSWC.RemoteBlazorWebView.Wpf.BlazorWebView V = new(); }
""", load: false);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW003");
    }

    [Theory]
    [InlineData("""Mode = RemoteBlazorMode.Remote""", "RBW004")]
    [InlineData("""Mode = RemoteBlazorMode.Remote, ServerUri = "not a uri" """, "RBW005")]
    [InlineData("""Id = "not-a-guid" """, "RBW006")]
    [InlineData("""ServerUri = "https://localhost:5001" """, "RBW008")]
    public void Bad_options_are_reported(string options, string id)
    {
        var r = GeneratorHarness.Run(Wpf($"[RemoteBlazor(nameof(RemoteBlazorWebView), {options})]"), load: false);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == id);
    }

    private const string ReverseMembers = """
        internal PeakSWC.RemoteBlazorWebView.Wpf.BlazorWebView RemoteBlazorWebView = new();
        internal Microsoft.Web.WebView2.Wpf.WebView2 WebView = new();
""";

    [Fact]
    public void Reverse_mirror_drives_the_app_in_the_WebView2()
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", ReverseMirror = nameof(WebView))]""", ReverseMembers));
        NoErrors(r);
        Assert.Empty(r.GeneratorDiagnostics);
        var type = r.Assembly!.GetType("App.MainWindow")!;
        var window = Activator.CreateInstance(type)!;
        Assert.True((bool)type.GetMethod("InitializeRemoteBlazor", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(window, [null])!);
        dynamic view = type.GetField("RemoteBlazorWebView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        dynamic webView2 = type.GetField("WebView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(window)!;
        Assert.True((bool)view.EnableMirrors);
        Assert.True((bool)view.HasConnected); // opens the browser on the mirror; not fired here, it would start a process

        var id = Guid.NewGuid();
        view.FireReady(new Uri("https://localhost:5001/"), id);
        Assert.Equal(new Uri($"https://localhost:5001/app/{id}"), (Uri)webView2.Source);
        Assert.Null((string?)view.Html); // no link: the WebView2 shows the app
    }

    [Theory]
    [InlineData("ShareControl = true", "?role=owner")]
    [InlineData("Unattended = true", "?role=owner&control=remote")]
    public void Share_control_shows_the_app_in_this_window_as_the_owner(string option, string query)
    {
        var r = GeneratorHarness.Run(Wpf($$"""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", {{option}})]"""));
        NoErrors(r);
        Assert.Empty(r.GeneratorDiagnostics);
        var (remote, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");
        Assert.True(remote);
        Assert.True((bool)view.EnableMirrors); // the browser joins at the mirror link
        Assert.False((bool)view.HasConnected);

        var id = Guid.NewGuid();
        view.FireReady(new Uri("https://localhost:5001/"), id);
        Assert.Equal(new Uri($"https://localhost:5001/app/{id}{query}"), (Uri)view.NavigatedUrl);
        Assert.Null((string?)view.Html); // no link page: the session bar has Copy link
    }

    [Theory]
    [InlineData("ShareControl = true, Mirror = true")]
    [InlineData("Unattended = true, Mirror = true")]
    public void Share_control_with_mirror_is_an_error(string options)
    {
        var r = GeneratorHarness.Run(Wpf($"[RemoteBlazor(nameof(RemoteBlazorWebView), {options})]"), load: false);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW011");
    }

    [Fact]
    public void Reverse_mirror_and_mirror_together_is_an_error()
    {
        var r = GeneratorHarness.Run(Wpf("[RemoteBlazor(nameof(RemoteBlazorWebView), Mirror = true, ReverseMirror = nameof(WebView))]", ReverseMembers), load: false);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW009");
    }

    [Fact]
    public void Reverse_mirror_must_be_a_WebView2()
    {
        var r = GeneratorHarness.Run(Wpf("[RemoteBlazor(nameof(RemoteBlazorWebView), ReverseMirror = nameof(Other))]",
            "internal PeakSWC.RemoteBlazorWebView.Wpf.BlazorWebView RemoteBlazorWebView = new(); internal string Other = \"\";"), load: false);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW010");
    }

    [Fact]
    public void Missing_reverse_mirror_control_warns_and_generates_a_no_op()
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", ReverseMirror = "WebView")]"""));
        NoErrors(r);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW001");
        Assert.False(GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow").Remote);
    }

    private const string Restartable = """[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", ShareControl = true, RestartOnRefresh = true)]""";

    [Fact]
    public void A_restart_carries_this_runs_own_command_line_on_the_session_the_server_assigned()
    {
        var r = GeneratorHarness.Run(Wpf(Restartable));
        NoErrors(r);
        GeneratorHarness.CommandLine(r.Assembly!, "-u=https://relay.example", "-i", "00000000-0000-0000-0000-000000000000", "--voice", "--unattended", "extra");
        var probe = GeneratorHarness.ProbeRestarts(r.Assembly!);
        var (_, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");

        var assigned = Guid.NewGuid();
        view.FireReady(new Uri("https://relay.example/"), assigned);
        view.FireRefreshed();

        Assert.Equal(["-u=https://relay.example/", $"-i={assigned}", "--voice", "--unattended", "extra"], Assert.Single(probe.Copies));
    }

    [Fact]
    public void An_app_can_choose_the_arguments_of_the_restarted_copy()
    {
        var r = GeneratorHarness.Run(Wpf(Restartable));
        NoErrors(r);
        GeneratorHarness.CommandLine(r.Assembly!);
        var probe = GeneratorHarness.ProbeRestarts(r.Assembly!);
        var id = Guid.NewGuid();
        var startup = GeneratorHarness.Startup(r.Assembly!, id: id, restartArguments: (uri, session) => [$"-u={uri}", $"-i={session}", "--restarted"]);
        var (_, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow", startup: startup);

        view.FireRefreshed();

        Assert.Equal(["-u=https://localhost:5001/", $"-i={id}", "--restarted"], Assert.Single(probe.Copies));
    }

    [Fact]
    public void A_window_that_does_not_respond_is_restarted_from_the_background_and_only_once()
    {
        var r = GeneratorHarness.Run(Wpf(Restartable));
        NoErrors(r);
        GeneratorHarness.CommandLine(r.Assembly!);
        var probe = GeneratorHarness.ProbeRestarts(r.Assembly!);
        var (_, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");

        ((Action)view.RestartWhenUnresponsive)(); // what the library calls when the UI thread did not pick up the request

        Assert.Single(probe.Copies);
        Assert.Equal(1, probe.Exits); // the window cannot be closed from there: the process is ended
        Assert.Equal(0, GeneratorHarness.StaticInt(r.Assembly!, "System.Windows.Window", "Closes"));

        view.FireRefreshed(); // the UI thread wakes up afterwards
        Assert.Single(probe.Copies);
        Assert.Equal(0, GeneratorHarness.StaticInt(r.Assembly!, "System.Windows.Window", "Closes"));
    }

    [Fact]
    public void Without_RestartOnRefresh_there_is_no_restart_and_no_fallback()
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001")]"""));
        NoErrors(r);
        var (_, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");
        Assert.False((bool)view.HasRefreshed);
        Assert.Null((Action?)view.RestartWhenUnresponsive);
    }

    [Theory]
    [InlineData(new string[0], "?role=owner", false)]
    [InlineData(new[] { "--unattended" }, "?role=owner&control=remote", false)]
    [InlineData(new[] { "--voice" }, "?role=owner", true)]
    [InlineData(new[] { "/voice", "-unattended" }, "?role=owner&control=remote", true)]
    [InlineData(new[] { "--voice=false", "--unattended=0" }, "?role=owner", false)]
    public void Unattended_and_voice_can_be_chosen_on_the_command_line(string[] args, string query, bool voice)
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", ShareControl = true)]"""));
        NoErrors(r);
        GeneratorHarness.CommandLine(r.Assembly!, args);
        var (_, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");

        var id = Guid.NewGuid();
        view.FireReady(new Uri("https://localhost:5001/"), id);
        Assert.Equal(new Uri($"https://localhost:5001/app/{id}{query}"), (Uri)view.NavigatedUrl);
        Assert.Equal(voice, (bool)view.EnableVoice);
    }

    [Fact]
    public void The_voice_option_turns_voice_on_without_a_switch()
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", Voice = true)]"""));
        NoErrors(r);
        Assert.Empty(r.GeneratorDiagnostics);
        GeneratorHarness.CommandLine(r.Assembly!);
        var (_, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow");
        Assert.True((bool)view.EnableVoice);
    }

    [Fact]
    public void Values_the_app_supplies_win_over_the_command_line_and_the_attribute()
    {
        var r = GeneratorHarness.Run(Wpf("""[RemoteBlazor(nameof(RemoteBlazorWebView), ShareControl = true, Unattended = true, Voice = true)]"""));
        NoErrors(r);
        GeneratorHarness.CommandLine(r.Assembly!, "--voice", "--unattended"); // no -u=: Runtime mode would be local
        var id = Guid.NewGuid();
        var startup = GeneratorHarness.Startup(r.Assembly!, serverUri: new Uri("https://relay.example"), id: id, unattended: false, voice: false);
        var (remote, view) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow", startup: startup);

        Assert.True(remote); // the app said which server, e.g. from an environment variable or saved state
        Assert.Equal(new Uri("https://relay.example"), (Uri)view.ServerUri);
        Assert.Equal(id, (Guid)view.Id);
        Assert.False((bool)view.EnableVoice);
        view.FireReady(new Uri("https://relay.example/"), id);
        Assert.Equal(new Uri($"https://relay.example/app/{id}?role=owner"), (Uri)view.NavigatedUrl);
    }

    [Fact]
    public void A_startup_with_nothing_filled_in_behaves_like_no_startup()
    {
        var r = GeneratorHarness.Run(Wpf("[RemoteBlazor(nameof(RemoteBlazorWebView))]"));
        NoErrors(r);
        GeneratorHarness.CommandLine(r.Assembly!);
        var (remote, _) = GeneratorHarness.Initialize(r.Assembly!, "App.MainWindow", startup: GeneratorHarness.Startup(r.Assembly!));
        Assert.False(remote);
    }

    [Fact]
    public void Voice_on_a_WinForms_control_warns_and_is_ignored()
    {
        var r = GeneratorHarness.Run("""
using PeakSWC.RemoteBlazorWebView;
namespace App
{
    [RemoteBlazor("blazorWebView1", Mode = RemoteBlazorMode.Remote, ServerUri = "https://localhost:5001", Voice = true)]
    public partial class Form1 : System.Windows.Forms.Form
    {
        private PeakSWC.RemoteBlazorWebView.WindowsForms.BlazorWebView blazorWebView1 = new();
    }
}
""");
        NoErrors(r);
        Assert.Contains(r.GeneratorDiagnostics, d => d.Id == "RBW012");
        Assert.True(GeneratorHarness.Initialize(r.Assembly!, "App.Form1", "blazorWebView1").Remote);
    }
}
