using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;

namespace StaticContentCache.Tests;

public class OptimizedCompositeFileProviderTests
{
    [Theory]
    [InlineData("_framework/blazor.modules.json")]
    [InlineData("/_framework/blazor.modules.json")]
    [InlineData(@"\_framework\blazor.modules.json")]
    [InlineData("_FRAMEWORK/blazor.modules.json")]
    public void App_modules_manifest_takes_precedence_over_embedded_empty_fallback(string subpath)
    {
        var appManifest = """["App.lib.module.js"]"""u8.ToArray();
        var appProvider = new InMemoryFileProvider(("_framework/blazor.modules.json", appManifest));
        var composite = new OptimizedCompositeFileProvider(
            new EmbeddedFileProvider(typeof(RemoteWebView).Assembly),
            appProvider);

        var file = composite.GetFileInfo(subpath);

        Assert.True(file.Exists);
        Assert.Equal(appManifest, ReadAllBytes(file));
    }

    [Theory]
    [InlineData("_framework/blazor.modules.json")]
    [InlineData("/_framework/blazor.modules.json")]
    [InlineData(@"\_framework\blazor.modules.json")]
    [InlineData("_FRAMEWORK/blazor.modules.json")]
    public void Embedded_empty_manifest_is_returned_when_app_provider_omits_it(string subpath)
    {
        var appProvider = new InMemoryFileProvider(("index.html", "<html></html>"u8.ToArray()));
        var composite = new OptimizedCompositeFileProvider(
            new EmbeddedFileProvider(typeof(RemoteWebView).Assembly),
            appProvider);

        var file = composite.GetFileInfo(subpath);

        Assert.True(file.Exists);
        Assert.Equal("[]"u8.ToArray(), ReadAllBytes(file));
    }

    [Fact]
    public void Non_embedded_provider_order_is_preserved_for_modules_manifest()
    {
        var firstManifest = """["First.lib.module.js"]"""u8.ToArray();
        var secondManifest = """["Second.lib.module.js"]"""u8.ToArray();
        var composite = new OptimizedCompositeFileProvider(
            new EmbeddedFileProvider(typeof(RemoteWebView).Assembly),
            new InMemoryFileProvider(("_framework/blazor.modules.json", firstManifest)),
            new InMemoryFileProvider(("_framework/blazor.modules.json", secondManifest)));

        var file = composite.GetFileInfo("_framework/blazor.modules.json");

        Assert.True(file.Exists);
        Assert.Equal(firstManifest, ReadAllBytes(file));
    }

    [Theory]
    [InlineData("_framework/blazor.webview.js")]
    [InlineData("/_framework/blazor.webview.js")]
    [InlineData(@"\_framework\blazor.webview.js")]
    [InlineData("_FRAMEWORK/blazor.webview.js")]
    public void Embedded_remote_desktop_bundle_overrides_app_blazor_webview_js(string subpath)
    {
        var embeddedProvider = new EmbeddedFileProvider(typeof(RemoteWebView).Assembly);
        var expectedBundle = ReadAllBytes(embeddedProvider.GetFileInfo("_framework/blazor.webview.js"));
        var upstreamStub = "// upstream blazor.webview.js"u8.ToArray();
        Assert.NotEmpty(expectedBundle);
        Assert.NotEqual(upstreamStub, expectedBundle);

        var embeddedFirst = new OptimizedCompositeFileProvider(
            embeddedProvider,
            new InMemoryFileProvider(("_framework/blazor.webview.js", upstreamStub)));
        var appFirst = new OptimizedCompositeFileProvider(
            new InMemoryFileProvider(("_framework/blazor.webview.js", upstreamStub)),
            embeddedProvider);

        Assert.Equal(expectedBundle, ReadAllBytes(embeddedFirst.GetFileInfo(subpath)));
        Assert.Equal(expectedBundle, ReadAllBytes(appFirst.GetFileInfo(subpath)));
    }

    [Fact]
    public void Other_framework_and_app_files_fall_through_to_app_provider()
    {
        var customFrameworkJs = "export const ok = true;"u8.ToArray();
        var appCss = "body { margin: 0; }"u8.ToArray();
        var composite = new OptimizedCompositeFileProvider(
            new EmbeddedFileProvider(typeof(RemoteWebView).Assembly),
            new InMemoryFileProvider(
                ("_framework/custom.js", customFrameworkJs),
                ("css/app.css", appCss)));

        Assert.Equal(customFrameworkJs, ReadAllBytes(composite.GetFileInfo("/_framework/custom.js")));
        Assert.Equal(appCss, ReadAllBytes(composite.GetFileInfo(@"\css\app.css")));
        Assert.False(composite.GetFileInfo("_framework/does-not-exist.js").Exists);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_or_whitespace_subpath_throws_argument_exception(string subpath)
    {
        var composite = new OptimizedCompositeFileProvider(new EmbeddedFileProvider(typeof(RemoteWebView).Assembly));
        Assert.Throws<ArgumentException>(() => composite.GetFileInfo(subpath));
    }

    [Theory]
    [InlineData("/")]
    [InlineData(@"\")]
    public void Slash_only_subpath_returns_not_found(string subpath)
    {
        var composite = new OptimizedCompositeFileProvider(new EmbeddedFileProvider(typeof(RemoteWebView).Assembly));
        Assert.False(composite.GetFileInfo(subpath).Exists);
    }

    private static byte[] ReadAllBytes(IFileInfo file)
    {
        Assert.True(file.Exists);
        using var stream = file.CreateReadStream();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed class InMemoryFileProvider : IFileProvider
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public InMemoryFileProvider(params (string Path, byte[] Bytes)[] files)
        {
            foreach (var (path, bytes) in files)
                _files[Normalize(path)] = bytes;
        }

        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

        public IFileInfo GetFileInfo(string subpath)
        {
            var key = Normalize(subpath);
            return _files.TryGetValue(key, out var bytes) ? new InMemoryFile(key, bytes) : new NotFoundFileInfo(key);
        }

        private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

        private sealed class InMemoryFile(string path, byte[] bytes) : IFileInfo
        {
            public bool Exists => true;
            public long Length => bytes.LongLength;
            public string? PhysicalPath => null;
            public string Name => Path.GetFileName(path);
            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
            public bool IsDirectory => false;
            public Stream CreateReadStream() => new MemoryStream(bytes, writable: false);
        }
    }
}
