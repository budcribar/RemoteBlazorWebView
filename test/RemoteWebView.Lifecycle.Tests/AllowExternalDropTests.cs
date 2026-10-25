using EditWebView;

namespace RemoteWebView.Lifecycle.Tests;

public class AllowExternalDropTests
{
    // Same tabs the MAUI WPF BlazorWebView.cs uses for this block. Editor.cs matches it literally.
    private const string WpfTemplateChild =
        "if (GetTemplateChild(WebViewTemplateChildName) is not IWebView2 webView)\n" +
        "\t\t\t\t{\n" +
        "\t\t\t\t\tthrow new InvalidOperationException($\"Template child '{WebViewTemplateChildName}' was not found or does not implement {nameof(IWebView2)}. Ensure the control template contains a WebView2 or WebView2CompositionControl element named '{WebViewTemplateChildName}'.\");\n" +
        "\t\t\t\t}\n" +
        "\t\t\t\t_webview = webView;";

    [Fact]
    public void Editor_sets_AllowExternalDrop_on_the_wpf_IWebView2_template_child()
    {
        var output = RunEditor(
            inputRelativeDirectory: "",
            inputFileName: "BlazorWebView.cs",
            outputFileName: "BlazorWebViewBase.cs",
            source: "using System;\n" + WpfTemplateChild.Replace("\r\n", "\n"));

        var assignment = output.IndexOf("webView.AllowExternalDrop = false;", StringComparison.Ordinal);
        var store = output.IndexOf("_webview = webView;", StringComparison.Ordinal);
        Assert.True(assignment >= 0, "Converter did not emit webView.AllowExternalDrop = false.");
        Assert.True(store > assignment, "AllowExternalDrop must be set after the IWebView2 check and before the field is stored.");
        Assert.Contains("is not IWebView2 webView", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Editor_sets_AllowExternalDrop_on_the_winforms_constructor()
    {
        var output = RunEditor(
            inputRelativeDirectory: "WindowsForms",
            inputFileName: "BlazorWebView.cs",
            outputFileName: "BlazorWebViewFormBase.cs",
            source: "using System;\n_webview = new WebView2Control()\n{\n\tDock = DockStyle.Fill,\n};\n");

        Assert.Contains("Dock = DockStyle.Fill, AllowExternalDrop = false", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Checked_in_sources_disable_external_drop_for_wpf_and_winforms()
    {
        var root = RepoRoot();
        var wpf = File.ReadAllText(Path.Combine(root, "src", "RemoteBlazorWebView.Wpf", "BlazorWebViewBase.cs"));
        var winforms = File.ReadAllText(Path.Combine(root, "src", "RemoteBlazorWebView.WinForms", "BlazorWebViewFormBase.cs"));

        var check = wpf.IndexOf("is not IWebView2 webView", StringComparison.Ordinal);
        var assignment = wpf.IndexOf("webView.AllowExternalDrop = false;", StringComparison.Ordinal);
        var store = wpf.IndexOf("_webview = webView;", assignment >= 0 ? assignment : 0, StringComparison.Ordinal);
        Assert.True(check >= 0 && assignment > check && store > assignment);

        Assert.Contains("AllowExternalDrop = false", winforms, StringComparison.Ordinal);
    }

    private static string RunEditor(string inputRelativeDirectory, string inputFileName, string outputFileName, string source)
    {
        var root = Path.Combine(Path.GetTempPath(), "editwebview-allow-drop", Guid.NewGuid().ToString("n"));
        var inputDir = Path.Combine(root, "in", inputRelativeDirectory);
        var outputDir = Path.Combine(root, "out");
        Directory.CreateDirectory(inputDir);
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(inputDir, inputFileName), source.Replace("\r\n", "\n"));

        var editor = new Editor(Path.Combine(inputDir, inputFileName));
        editor.Edit();
        editor.WriteAllText(outputDir);
        return File.ReadAllText(Path.Combine(outputDir, outputFileName));
    }

    internal static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RemoteBlazorWebView.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root from the test output directory.");
    }
}
