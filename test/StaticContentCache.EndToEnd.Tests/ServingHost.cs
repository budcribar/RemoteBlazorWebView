extern alias client;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Services;
#if !NET9_0
using PeakSWC.RemoteWebView.RemoteStaticFiles;
#endif
using FileProvider = Microsoft.Extensions.FileProviders.IFileProvider;
using FileInfo = Microsoft.Extensions.FileProviders.IFileInfo;
using ClientSync = client::PeakSWC.RemoteWebView.ClientFileSyncManager;

namespace StaticContentCache.EndToEnd.Tests;

public sealed class ServingHost : IAsyncLifetime
{
    private static readonly byte[] HostPage = """<html><head><base href="/" /></head><body></body></html>"""u8.ToArray();

    private readonly List<ClientSync> _syncs = [];
    private WebApplication? _app;
    private GrpcChannel? _channel;
    private HttpClient? _http;

    public Guid BaselineId { get; } = Guid.NewGuid();
    public Guid EmptyId { get; } = Guid.NewGuid();
    public Guid OtherId { get; } = Guid.NewGuid();
    public Guid NestedId { get; } = Guid.NewGuid();
    public Guid ObjectId { get; } = Guid.NewGuid();
    public Guid AppId { get; } = Guid.NewGuid();
    public Guid PackageId { get; } = Guid.NewGuid();
    public MemoryFileProvider AppFiles { get; } = new();
    public string Url { get; private set; } = "";

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var script = WebViewPackageFiles.BlazorWebViewScript();
        var packageManifest = WebViewPackageFiles.ModulesManifest();
        var baselineModule = ServingFiles.ReadShared("Baseline.lib.module.js");
        var baselineManifest = ServingFiles.ReadShared("blazor.modules.json");
        var clients = new Dictionary<Guid, MemoryFileProvider>
        {
            [BaselineId] = Page(baselineManifest, script, ("Baseline.lib.module.js", baselineModule)),
            [EmptyId] = Page(ServingFiles.Read("empty-blazor.modules.json"), script),
            [OtherId] = Page(ServingFiles.Manifest("Other.lib.module.js"), script, ("Other.lib.module.js", ServingFiles.Read("Other.lib.module.js"))),
            [NestedId] = Page(ServingFiles.Manifest("_content/Lib/Nested.lib.module.js"), script, ("_content/Lib/Nested.lib.module.js", ServingFiles.Read("Nested.lib.module.js"))),
            [ObjectId] = Page(ServingFiles.Read("object-blazor.modules.json"), script, ("Baseline.lib.module.js", baselineModule)),
            [AppId] = AppFiles
        };
        AppFiles.Add("index.html", HostPage);
        AppFiles.Add("_framework/blazor.webview.js", script);
        AppFiles.Add("_framework/blazor.modules.json", """[{"name":"_content/Lib/Lib.lib.module.js"},{"name":"App.lib.module.js"}]"""u8.ToArray());
        AppFiles.Add("_content/Lib/Lib.lib.module.js", """export function beforeStart() { globalThis.libBefore = true; } export function afterStarted() { globalThis.libAfter = true; }"""u8.ToArray());
        AppFiles.Add("App.lib.module.js", """export function beforeStart() { globalThis.appBefore = true; } export function afterStarted() { globalThis.appAfter = true; }"""u8.ToArray());

        clients[PackageId] = Page(packageManifest, script);

        var ids = clients.Keys.ToArray();
        var ready = ids.Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();
        var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        for (var i = 0; i < ids.Length; i++)
            states[ids[i].ToString()] = Seed(ready[i]);

        var grpcPort = FreePort();
        var httpPort = FreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls();
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
            options.Listen(IPAddress.Loopback, httpPort, listen => listen.Protocols = HttpProtocols.Http1);
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
        _app = builder.Build();
        _app.UseMiddleware<RemoteFilesMiddleware>();
        _app.MapGrpcService<RemoteWebViewService>();
        await _app.StartAsync();
        Url = $"http://127.0.0.1:{httpPort}";

        _channel = GrpcChannel.ForAddress($"http://127.0.0.1:{grpcPort}", new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, UseCookies = false }
        });
        var ipc = new client::PeakSWC.RemoteWebView.WebViewIPC.WebViewIPCClient(_channel);
        foreach (var (id, files) in clients)
        {
            var sync = new ClientSync(ipc, id, "index.html", files, static _ => { }, NullLogger.Instance);
            sync.HandleServerRequests(CancellationToken.None);
            _syncs.Add(sync);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        foreach (var one in ready)
            await one.Task.WaitAsync(timeout.Token);

        _http = new HttpClient(new SocketsHttpHandler())
        {
            BaseAddress = new Uri(Url + "/"),
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

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
        foreach (var sync in _syncs)
            await sync.CloseAsync();
        _http?.Dispose();
        _channel?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private static MemoryFileProvider Page(byte[] manifest, byte[] script, params (string Path, byte[] Bytes)[] files)
    {
        var provider = new MemoryFileProvider();
        provider.Add("index.html", HostPage);
        provider.Add("_framework/blazor.webview.js", script);
        provider.Add("_framework/blazor.modules.json", manifest);
        foreach (var (path, bytes) in files)
            provider.Add(path, bytes);
        return provider;
    }

    private static TaskCompletionSource<ServiceState> Seed(TaskCompletionSource<bool> ready)
    {
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
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

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public sealed class MemoryFileProvider : FileProvider
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public void Add(string path, byte[] bytes) => _files[Normalize(path)] = bytes;

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

internal static class ServingFiles
{
    public static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AssetDirectory(), name));

    public static byte[] ReadShared(string name) => File.ReadAllBytes(Path.Combine(SharedAssetDirectory(), name));

    public static byte[] Manifest(params string[] names) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(names);

    private static string SharedAssetDirectory()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException(dir);
        return dir;
    }

    private static string AssetDirectory() => Path.Combine(SharedAssetDirectory(), "ModuleInitializers");
}
