using System.Reflection;

namespace StaticContentCache.EndToEnd.Tests;

internal static class WebViewPackageFiles
{
    public static string Version()
    {
        var csproj = FindUp("src", "RemoteWebView", "RemoteWebView.csproj");
        var text = File.ReadAllText(csproj);
        var direct = Quoted(text, "Microsoft.AspNetCore.Components.WebView\" Version=\"");
        if (direct is not null)
            return direct;

        var forms = Quoted(text, "Microsoft.AspNetCore.Components.WebView.WindowsForms\" Version=\"");
        if (forms is null)
            throw new InvalidOperationException("WebView package version was not found in " + csproj);

        var nuspec = Path.Combine(PackageRoot(), "microsoft.aspnetcore.components.webview.windowsforms", forms, "microsoft.aspnetcore.components.webview.windowsforms.nuspec");
        var dependency = Quoted(File.ReadAllText(nuspec), "id=\"Microsoft.AspNetCore.Components.WebView\" version=\"");
        return dependency ?? throw new InvalidOperationException("WebView dependency was not found in " + nuspec);
    }

    public static byte[] BlazorWebViewScript() => Read("blazor.webview.js");

    public static byte[] ModulesManifest() => Read("blazor.modules.json");

    private static byte[] Read(string file)
    {
        var root = Path.Combine(PackageRoot(), "microsoft.aspnetcore.components.webview", Version());
        foreach (var folder in new[] { "staticwebassets", "build" })
        {
            var path = Path.Combine(root, folder, file);
            if (File.Exists(path))
                return File.ReadAllBytes(path);
        }

        var dll = Directory.EnumerateFiles(Path.Combine(root, "lib"), "Microsoft.AspNetCore.Components.WebView.dll", SearchOption.AllDirectories).Single();
        var assembly = Assembly.LoadFrom(dll);
        using var stream = assembly.GetManifestResourceStream("_framework/" + file)
            ?? throw new FileNotFoundException(file + " is not in " + dll);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static string? Quoted(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += marker.Length;
        var end = text.IndexOf('"', start);
        return text[start..end];
    }

    private static string PackageRoot()
    {
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env))
            return env;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    private static string FindUp(params string[] relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relative).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException(string.Join('/', relative));
    }
}
