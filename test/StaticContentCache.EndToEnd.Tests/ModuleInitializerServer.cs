extern alias client;

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;
using FileProvider = Microsoft.Extensions.FileProviders.IFileProvider;
using FileInfo = Microsoft.Extensions.FileProviders.IFileInfo;
using ClientSync = client::PeakSWC.RemoteWebView.ClientFileSyncManager;

namespace StaticContentCache.EndToEnd.Tests;

public sealed class ModuleInitializerServer : IAsyncLifetime
{
    private readonly List<ClientSync> _syncs = [];
    private DuplexHost? _host;
    private GrpcChannel? _channel;
    private HttpClient? _http;

    public Guid BaselineId { get; } = Guid.NewGuid();
    public Guid OrderId { get; } = Guid.NewGuid();
    public Guid AsyncId { get; } = Guid.NewGuid();
    public Guid ThrowingId { get; } = Guid.NewGuid();
    public Guid MissingId { get; } = Guid.NewGuid();
    public Guid EmptyId { get; } = Guid.NewGuid();
    public Guid NestedId { get; } = Guid.NewGuid();
    public Guid ArgsId { get; } = Guid.NewGuid();
    public Guid ChangedId { get; } = Guid.NewGuid();
    public Guid OtherId { get; } = Guid.NewGuid();
    public Guid ObjectManifestId { get; } = Guid.NewGuid();
    public MutableFileProvider ChangedFiles { get; } = new();
    public string Http1Url { get; private set; } = "";
    private ChromeSession? _chrome;
    private readonly SemaphoreSlim _chromeGate = new(1, 1);

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var index = ModuleInitializerFiles.Read("index.html");
        var probe = ModuleInitializerFiles.Read("module-initializer-probe.js");
        var webView = File.ReadAllBytes(ModuleInitializerFiles.WebViewScriptPath());
        var clients = new Dictionary<Guid, MutableFileProvider>
        {
            [BaselineId] = Page(index, probe, webView, ModuleInitializerFiles.ReadShared("blazor.modules.json"),
                ("Baseline.lib.module.js", ModuleInitializerFiles.ReadShared("Baseline.lib.module.js"))),
            [OrderId] = Page(index, probe, webView, Manifest("First.lib.module.js", "Second.lib.module.js"),
                ("First.lib.module.js", ModuleInitializerFiles.Read("First.lib.module.js")),
                ("Second.lib.module.js", ModuleInitializerFiles.Read("Second.lib.module.js"))),
            [AsyncId] = Page(index, probe, webView, Manifest("Async.lib.module.js", "Fast.lib.module.js"),
                ("Async.lib.module.js", ModuleInitializerFiles.Read("Async.lib.module.js")),
                ("Fast.lib.module.js", ModuleInitializerFiles.Read("Fast.lib.module.js"))),
            [ThrowingId] = Page(index, probe, webView, Manifest("Sibling.lib.module.js", "Throwing.lib.module.js"),
                ("Sibling.lib.module.js", ModuleInitializerFiles.Read("Sibling.lib.module.js")),
                ("Throwing.lib.module.js", ModuleInitializerFiles.Read("Throwing.lib.module.js"))),
            [MissingId] = Page(index, probe, webView, Manifest("Sibling.lib.module.js", "missing.lib.module.js"),
                ("Sibling.lib.module.js", ModuleInitializerFiles.Read("Sibling.lib.module.js"))),
            [EmptyId] = Page(index, probe, webView, ModuleInitializerFiles.Read("empty-blazor.modules.json")),
            [NestedId] = Page(index, probe, webView, Manifest("_content/Lib/Nested.lib.module.js"),
                ("_content/Lib/Nested.lib.module.js", ModuleInitializerFiles.Read("Nested.lib.module.js"))),
            [ArgsId] = Page(index, probe, webView, Manifest("Args.lib.module.js"),
                ("Args.lib.module.js", ModuleInitializerFiles.Read("Args.lib.module.js"))),
            [OtherId] = Page(index, probe, webView, Manifest("Other.lib.module.js"),
                ("Other.lib.module.js", ModuleInitializerFiles.Read("Other.lib.module.js"))),
            [ObjectManifestId] = Page(index, probe, webView, ModuleInitializerFiles.Read("object-blazor.modules.json"),
                ("Baseline.lib.module.js", ModuleInitializerFiles.Read("Baseline.lib.module.js")))
        };
        Page(ChangedFiles, index, probe, webView, Manifest("Changed.lib.module.js"),
            ("Changed.lib.module.js", ModuleInitializerFiles.Read("Changed.lib.module.js")));
        clients[ChangedId] = ChangedFiles;

        var ids = clients.Keys.ToArray();
        _host = await DuplexHost.StartAsync(ids);
        (Http1Url, var http2) = await IdentifyAsync(_host.Urls);
        _channel = GrpcChannel.ForAddress(http2, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, UseCookies = false }
        });
        var ipc = new client::PeakSWC.RemoteWebView.WebViewIPC.WebViewIPCClient(_channel);
        foreach (var (id, files) in clients)
        {
            var sync = new ClientSync(ipc, id, "index.html", files, static _ => { }, NullLogger.Instance,
                static _ => client::PeakSWC.RemoteWebView.StaticContentCacheControl.Default);
            sync.HandleServerRequests(CancellationToken.None);
            _syncs.Add(sync);
        }

        await _host.WaitUntilReady();
        _http = new HttpClient(new SocketsHttpHandler())
        {
            BaseAddress = new Uri(Http1Url.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<ChromeSession> ChromeAsync()
    {
        await _chromeGate.WaitAsync();
        try
        {
            return _chrome ??= await ChromeSession.StartAsync();
        }
        finally
        {
            _chromeGate.Release();
        }
    }

    public string PageUrl(Guid id) => $"{Http1Url.TrimEnd('/')}/{id}/index.html";

    public Task<HttpResponseMessage> GetAsync(Guid id, string relative)
        => _http!.GetAsync($"{id}/{relative}");

    public Task<HttpResponseMessage> ConditionalGetAsync(Guid id, string relative, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{id}/{relative}");
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return _http!.SendAsync(request);
    }

    public async Task DisposeAsync()
    {
        if (_chrome is not null)
            await _chrome.DisposeAsync();
        foreach (var sync in _syncs)
            await sync.CloseAsync();
        _http?.Dispose();
        _channel?.Dispose();
        if (_host is not null)
            await _host.DisposeAsync();
        _chromeGate.Dispose();
    }

    private static MutableFileProvider Page(byte[] index, byte[] probe, byte[] webView, byte[] manifest, params (string Path, byte[] Bytes)[] files)
    {
        var provider = new MutableFileProvider();
        Page(provider, index, probe, webView, manifest, files);
        return provider;
    }

    private static void Page(MutableFileProvider provider, byte[] index, byte[] probe, byte[] webView, byte[] manifest, params (string Path, byte[] Bytes)[] files)
    {
        provider.Add("index.html", index);
        provider.Add("module-initializer-probe.js", probe);
        provider.Add("_framework/blazor.webview.js", webView);
        provider.Add("_framework/blazor.modules.json", manifest);
        foreach (var (path, bytes) in files)
            provider.Add(path, bytes);
    }

    private static byte[] Manifest(params string[] names) => JsonSerializer.SerializeToUtf8Bytes(names);

    private static async Task<(string Http1, string Http2)> IdentifyAsync(IEnumerable<string> urls)
    {
        string? http1 = null;
        string? http2 = null;
        foreach (var url in urls)
        {
            using var client = new HttpClient(new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(3) };
            try
            {
                using var response = await client.GetAsync(url);
                http1 ??= url;
            }
            catch (Exception)
            {
                http2 ??= url;
            }
        }

        if (http1 is null || http2 is null)
            throw new InvalidOperationException("Expected one HTTP/1 and one HTTP/2 listener. Urls: " + string.Join(", ", urls));
        return (http1, http2);
    }

    private sealed class DuplexHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly TaskCompletionSource<bool>[] _ready;
        public IReadOnlyList<string> Urls { get; }

        private DuplexHost(WebApplication app, TaskCompletionSource<bool>[] ready)
        {
            _app = app;
            _ready = ready;
            Urls = app.Urls.ToArray();
        }

        public static async Task<DuplexHost> StartAsync(IReadOnlyCollection<Guid> clients)
        {
            var ready = clients.Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
            var index = 0;
            foreach (var id in clients)
                states[id.ToString()] = Seed(ready[index++]);

            var builder = WebApplication.CreateBuilder();
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            builder.WebHost.UseUrls();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http1);
                options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2);
            });
            builder.Services.AddGrpc();
            builder.Services.AddLogging();
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton(states);
            builder.Services.AddSingleton(new ConcurrentDictionary<string, Channel<string>>());
            builder.Services.AddSingleton(new RemoteFilesOptions { UseClientCache = true, UseServerCache = false });
            builder.Services.AddSingleton<ServerFileSyncManager>();
            builder.Services.AddSingleton<RemoteFileResolver>();
            builder.Services.AddSingleton<ShutdownService>();
            builder.Services.AddSingleton<RemoteWebViewService>();
            var app = builder.Build();
            app.UseMiddleware<RemoteFilesMiddleware>();
            app.MapGrpcService<RemoteWebViewService>();
            await app.StartAsync();
            return new DuplexHost(app, ready);
        }

        public async Task WaitUntilReady()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            foreach (var ready in _ready)
                await ready.Task.WaitAsync(timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static TaskCompletionSource<ServiceState> Seed(TaskCompletionSource<bool> ready)
        {
            var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
            state.FileManagerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = state.FileManagerReady.Task.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                    ready.TrySetResult(task.Result);
                else if (task.Exception is not null)
                    ready.TrySetException(task.Exception);
            }, TaskScheduler.Default);
            var source = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(state);
            return source;
        }
    }
}

public sealed class MutableFileProvider : FileProvider
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public void Add(string path, byte[] bytes) => _files[Normalize(path)] = bytes;

    public void Replace(string path, byte[] bytes) => _files[Normalize(path)] = bytes;

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    public FileInfo GetFileInfo(string subpath)
    {
        var key = Normalize(subpath);
        return _files.TryGetValue(key, out var bytes) ? new MemoryFile(key, bytes) : new NotFoundFileInfo(key);
    }

    private static string Normalize(string path) => path.Replace('\\', '/').TrimStart('/');

    private sealed class MemoryFile(string path, byte[] bytes) : FileInfo
    {
        public bool Exists => true;
        public long Length => bytes.LongLength;
        public string? PhysicalPath => null;
        public string Name => Path.GetFileName(path);
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(bytes, writable: false);
    }
}

internal static class ModuleInitializerFiles
{
    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AssetDirectory(), name));

    public static byte[] ReadShared(string name) => File.ReadAllBytes(Path.Combine(SharedAssetDirectory(), name));

    public static string WebViewScriptPath() => PackagePath("staticwebassets", "blazor.webview.js");

    public static string PackageModulesManifestPath() => PackagePath("build", "blazor.modules.json");

    private static string SharedAssetDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(dir);
        return dir;
    }

    private static string AssetDirectory()
    {
        var dir = Path.Combine(SharedAssetDirectory(), "ModuleInitializers");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(dir);
        return dir;
    }

    private static string PackagePath(string folder, string file)
    {
        var version = WebViewPackageVersion();
        foreach (var root in PackageRoots())
        {
            var path = Path.Combine(root, "microsoft.aspnetcore.components.webview", version, folder, file);
            if (File.Exists(path))
                return path;
        }

        throw new FileNotFoundException($"Microsoft.AspNetCore.Components.WebView {version} {folder}/{file} was not in the NuGet package cache.");
    }

    private static string WebViewPackageVersion()
    {
        var csproj = FindUp("src", "RemoteWebView", "RemoteWebView.csproj");
        var text = File.ReadAllText(csproj);
        const string marker = "Microsoft.AspNetCore.Components.WebView\" Version=\"";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            throw new InvalidOperationException("WebView package version was not found in " + csproj);
        start += marker.Length;
        var end = text.IndexOf('"', start);
        return text[start..end];
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

    private static IEnumerable<string> PackageRoots()
    {
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env))
            yield return env;
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }
}
