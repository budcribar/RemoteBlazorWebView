extern alias client;

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using FileProvider = Microsoft.Extensions.FileProviders.IFileProvider;
using FileInfo = Microsoft.Extensions.FileProviders.IFileInfo;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Services;
using ClientSync = client::PeakSWC.RemoteWebView.ClientFileSyncManager;

namespace StaticContentCache.EndToEnd.Tests;

// blazor.webview.js fetches _framework/blazor.modules.json and maps each array element
// with { name: element }, then imports new URL(name, document.baseURI).
public class BaselineModuleInitializerTests : IAsyncLifetime
{
    private const string ModuleName = "Baseline.lib.module.js";
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();
    private static readonly byte[] HostPage = """<html><head><base href="/" /></head><body></body></html>"""u8.ToArray();

    private readonly Guid _clientId = Guid.NewGuid();
    private DuplexHost? _host;
    private GrpcChannel? _channel;
    private ClientSync? _sync;
    private HttpClient? _http;
    private byte[] _moduleBytes = [];
    private byte[] _manifestBytes = [];

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets");
        _moduleBytes = File.ReadAllBytes(Path.Combine(assets, ModuleName));
        _manifestBytes = File.ReadAllBytes(Path.Combine(assets, "blazor.modules.json"));
        var files = new MemoryFileProvider(new Dictionary<string, byte[]>
        {
            ["_framework/blazor.modules.json"] = _manifestBytes,
            [ModuleName] = _moduleBytes,
            ["index.html"] = HostPage
        });

        _host = await DuplexHost.StartAsync(_clientId);
        _channel = GrpcChannel.ForAddress(_host.Url, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, UseCookies = false }
        });
        var ipc = new client::PeakSWC.RemoteWebView.WebViewIPC.WebViewIPCClient(_channel);
        _sync = new ClientSync(ipc, _clientId, "index.html", files, static _ => { }, NullLogger.Instance);
        _sync.HandleServerRequests(CancellationToken.None);
        await _host.WaitUntilReady();
        _http = new HttpClient(new SocketsHttpHandler())
        {
            BaseAddress = new Uri(_host.Url),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
    }

    public async Task DisposeAsync()
    {
        if (_sync is not null)
            await _sync.CloseAsync();
        _http?.Dispose();
        _channel?.Dispose();
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [Fact]
    public async Task Baseline_initializer_is_served_byte_for_byte_and_listed_in_the_manifest()
    {
        var moduleHash = SHA256.HashData(_moduleBytes);
        Assert.NotEmpty(_moduleBytes);
        Assert.Equal(SHA256.HashSizeInBytes, moduleHash.Length);

        var listed = JsonSerializer.Deserialize<string[]>(_manifestBytes);
        Assert.NotNull(listed);
        Assert.Equal([ModuleName], listed);

        var index = await _http!.GetAsync($"/{_clientId}/index.html");
        var html = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        var marker = "base href=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, html);
        start += marker.Length;
        var baseHref = html[start..html.IndexOf('"', start)];
        Assert.Equal($"/{_clientId}/", baseHref);

        var moduleUri = new Uri(new Uri(_host!.Url), baseHref + ModuleName);
        var module = await _http.GetAsync(moduleUri);
        var servedModule = await module.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, module.StatusCode);
        Assert.Equal(_moduleBytes, servedModule);
        Assert.Equal(_moduleBytes.LongLength, module.Content.Headers.ContentLength);
        Assert.Equal(moduleHash, SHA256.HashData(servedModule));
        Assert.Equal(ContentType(ModuleName), module.Content.Headers.ContentType?.MediaType);
        Assert.Equal("text/javascript", module.Content.Headers.ContentType?.MediaType);

        var manifest = await _http.GetAsync($"/{_clientId}/_framework/blazor.modules.json");
        var servedManifest = await manifest.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        Assert.Equal(_manifestBytes, servedManifest);
        Assert.Equal(_manifestBytes.LongLength, manifest.Content.Headers.ContentLength);
        Assert.Equal(ContentType("blazor.modules.json"), manifest.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", manifest.Content.Headers.ContentType?.MediaType);
        var servedListed = JsonSerializer.Deserialize<string[]>(servedManifest);
        Assert.NotNull(servedListed);
        Assert.Equal([ModuleName], servedListed);
    }

    private static string ContentType(string path)
        => ContentTypes.TryGetContentType(path, out var contentType) ? contentType : "application/octet-stream";

    private sealed class DuplexHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly TaskCompletionSource<bool> _ready;
        public string Url { get; }

        private DuplexHost(WebApplication app, string url, TaskCompletionSource<bool> ready)
        {
            _app = app;
            Url = url;
            _ready = ready;
        }

        public static async Task<DuplexHost> StartAsync(Guid clientId)
        {
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>
            {
                [clientId.ToString()] = source
            };

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2);
            });
            builder.Services.AddGrpc();
            builder.Services.AddLogging();
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton(states);
            builder.Services.AddSingleton(new ConcurrentDictionary<string, Channel<string>>());
            builder.Services.AddSingleton(new RemoteFilesOptions { UseClientCache = false, UseServerCache = false });
            builder.Services.AddSingleton<ServerFileSyncManager>();
            builder.Services.AddSingleton<RemoteFileResolver>();
            builder.Services.AddSingleton<ShutdownService>();
            builder.Services.AddSingleton<RemoteWebViewService>();
            var app = builder.Build();
            app.UseMiddleware<RemoteFilesMiddleware>();
            app.MapGrpcService<RemoteWebViewService>();
            await app.StartAsync();
            return new DuplexHost(app, app.Urls.Single(), ready);
        }

        public async Task WaitUntilReady()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _ready.Task.WaitAsync(timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class MemoryFileProvider(Dictionary<string, byte[]> files) : FileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
        public FileInfo GetFileInfo(string subpath)
        {
            var key = subpath.TrimStart('/');
            return files.TryGetValue(key, out var bytes) ? new MemoryFile(key, bytes) : new NotFoundFileInfo(key);
        }
    }

    private sealed class MemoryFile(string path, byte[] bytes) : FileInfo
    {
        public bool Exists => true;
        public long Length => bytes.Length;
        public string? PhysicalPath => null;
        public string Name => Path.GetFileName(path);
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(bytes, writable: false);
    }
}
