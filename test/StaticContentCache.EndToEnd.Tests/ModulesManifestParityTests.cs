extern alias client;

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
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
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;
using ClientETag = client::PeakSWC.RemoteWebView.ETagGenerator;
using ClientSync = client::PeakSWC.RemoteWebView.ClientFileSyncManager;

namespace StaticContentCache.EndToEnd.Tests;

// Local rules copied from dotnet/aspnetcore
// src/Components/WebView/WebView/src/StaticContentProvider.cs (TryGetResponseContent,
// GetResponseContentTypeOrDefault, GetResponseHeaders). That type is internal to the
// WebView assembly, so the comparison is against those rules rather than a project reference.
public class ModulesManifestParityTests : IAsyncLifetime
{
    private const string LocalCacheControl = "no-cache, max-age=0, must-revalidate, no-store";
    private static readonly FileExtensionContentTypeProvider LocalContentTypes = new();

    private static readonly byte[] CustomManifest = """[{"name":"_content/Lib/Lib.lib.module.js"},{"name":"App.lib.module.js"}]"""u8.ToArray();
    // Microsoft.AspNetCore.Components.WebView 11 build/blazor.modules.json, the package fallback when the app has no JS initializers.
    private static readonly byte[] PackageFallbackManifest = "[]\r\n"u8.ToArray();
    private static readonly byte[] LibraryModule = """export function beforeStart() { globalThis.libBefore = true; } export function afterStarted() { globalThis.libAfter = true; }"""u8.ToArray();
    private static readonly byte[] AppModule = """export function beforeStart() { globalThis.appBefore = true; } export function afterStarted() { globalThis.appAfter = true; }"""u8.ToArray();
    private static readonly byte[] HostPage = """<html><head><base href="/" /></head><body></body></html>"""u8.ToArray();

    private readonly Guid _appClient = Guid.NewGuid();
    private readonly Guid _emptyClient = Guid.NewGuid();
    private DuplexHost? _host;
    private GrpcChannel? _channel;
    private ClientSync? _appSync;
    private ClientSync? _emptySync;
    private HttpClient? _http;
    private MemoryFileProvider? _appFiles;

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        _appFiles = new MemoryFileProvider(new Dictionary<string, byte[]>
        {
            ["_framework/blazor.modules.json"] = CustomManifest,
            ["_content/Lib/Lib.lib.module.js"] = LibraryModule,
            ["App.lib.module.js"] = AppModule,
            ["index.html"] = HostPage
        });
        var emptyFiles = new MemoryFileProvider(new Dictionary<string, byte[]>
        {
            ["_framework/blazor.modules.json"] = PackageFallbackManifest,
            ["index.html"] = HostPage
        });

        _host = await DuplexHost.StartAsync(_appClient, _emptyClient);
        _channel = GrpcChannel.ForAddress(_host.Url, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, UseCookies = false }
        });
        var ipc = new client::PeakSWC.RemoteWebView.WebViewIPC.WebViewIPCClient(_channel);
        _appSync = new ClientSync(ipc, _appClient, "index.html", _appFiles, static _ => { }, NullLogger.Instance,
            _ => client::PeakSWC.RemoteWebView.StaticContentCacheControl.Default);
        // Same call RemoteWebView makes when the desktop control's provider was left unset.
        _emptySync = new ClientSync(ipc, _emptyClient, "index.html", emptyFiles, static _ => { }, NullLogger.Instance,
            client::PeakSWC.RemoteWebView.StaticContentCacheControl.ProviderOrDefault(null));
        _appSync.HandleServerRequests(CancellationToken.None);
        _emptySync.HandleServerRequests(CancellationToken.None);
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
        if (_appSync is not null)
            await _appSync.CloseAsync();
        if (_emptySync is not null)
            await _emptySync.CloseAsync();
        _http?.Dispose();
        _channel?.Dispose();
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [Fact]
    public async Task Custom_manifest_is_served_byte_for_byte_per_client()
    {
        var app = await Get(_appClient, "_framework/blazor.modules.json");
        var empty = await Get(_emptyClient, "_framework/blazor.modules.json");
        var appBytes = await app.Content.ReadAsByteArrayAsync();
        var emptyBytes = await empty.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, app.StatusCode);
        Assert.Equal(CustomManifest, appBytes);
        Assert.Equal(CustomManifest.LongLength, app.Content.Headers.ContentLength);
        Assert.Equal(LocalContentType("_framework/blazor.modules.json"), app.Content.Headers.ContentType?.MediaType);
        Assert.Equal("application/json", app.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Equal(PackageFallbackManifest, emptyBytes);
        Assert.NotEqual(Encoding.ASCII.GetBytes("[]"), emptyBytes);
        Assert.NotEqual(appBytes, emptyBytes);

        var expected = ClientETag.GenerateETag(_appFiles!.GetFileInfo("_framework/blazor.modules.json"));
        Assert.NotEqual("etag", expected);
        Assert.Equal(expected, Header(app, "ETag"));
    }

    [Fact]
    public async Task Listed_modules_follow_the_rewritten_base_href()
    {
        var index = await Get(_appClient, "index.html");
        var html = await index.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, index.StatusCode);
        Assert.Equal(LocalContentType("index.html"), index.Content.Headers.ContentType?.MediaType);
        var baseHref = BaseHref(html);
        Assert.Equal($"/{_appClient}/", baseHref);

        foreach (var (relative, bytes) in new[]
        {
            ("_content/Lib/Lib.lib.module.js", LibraryModule),
            ("App.lib.module.js", AppModule)
        })
        {
            var resolved = new Uri(new Uri(_host!.Url), baseHref + relative);
            var response = await _http!.GetAsync(resolved);
            var body = await response.Content.ReadAsByteArrayAsync();

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(bytes, body);
            Assert.Equal(bytes.LongLength, response.Content.Headers.ContentLength);
            Assert.Equal(LocalContentType(relative), response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        }
    }

    [Fact]
    public async Task Missing_file_status_matches_the_local_provider()
    {
        var response = await Get(_appClient, "no-such.lib.module.js");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Missing_file_body_matches_the_local_provider()
    {
        const string relative = "no-such.lib.module.js";
        var response = await Get(_appClient, relative);
        Assert.Equal(LocalMissingBody(relative), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_file_content_type_matches_the_local_provider()
    {
        var response = await Get(_appClient, "no-such.lib.module.js");
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.ToString());
    }

    [Fact]
    public async Task Missing_file_cache_control_matches_the_local_provider()
    {
        var response = await Get(_appClient, "no-such.lib.module.js");
        AssertSameCacheDirectives(Header(response, "Cache-Control"), LocalCacheControl);
    }

    [Fact]
    public async Task Provider_cache_control_matches_the_local_provider()
    {
        Assert.Equal(LocalCacheControl, client::PeakSWC.RemoteWebView.StaticContentCacheControl.Default);

        var withProvider = await Get(_appClient, "_content/Lib/Lib.lib.module.js");
        AssertSameCacheDirectives(Header(withProvider, "Cache-Control"), LocalCacheControl);
    }

    [Fact]
    public async Task Stock_client_cache_control_matches_the_local_provider()
    {
        var stock = await Get(_emptyClient, "_framework/blazor.modules.json");
        AssertSameCacheDirectives(Header(stock, "Cache-Control"), LocalCacheControl);
    }

    [Fact]
    public async Task Not_modified_requires_the_client_etag()
    {
        var path = "_framework/blazor.modules.json";
        var first = await Get(_appClient, path);
        var expected = ClientETag.GenerateETag(_appFiles!.GetFileInfo(path));
        Assert.Equal(expected, Header(first, "ETag"));
        Assert.NotEqual("etag", Header(first, "ETag"));

        var matched = await ConditionalGet(_appClient, path, expected);
        Assert.Equal(HttpStatusCode.NotModified, matched.StatusCode);
        Assert.Empty(await matched.Content.ReadAsByteArrayAsync());

        var synthetic = await ConditionalGet(_appClient, path, "etag");
        Assert.Equal(HttpStatusCode.OK, synthetic.StatusCode);
        Assert.Equal(CustomManifest, await synthetic.Content.ReadAsByteArrayAsync());

        var other = await ConditionalGet(_appClient, path, Convert.ToBase64String(SHA256.HashData("other"u8.ToArray())));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(CustomManifest, await other.Content.ReadAsByteArrayAsync());
    }

    private Task<HttpResponseMessage> Get(Guid clientId, string relative)
        => _http!.GetAsync($"/{clientId}/{relative}");

    private Task<HttpResponseMessage> ConditionalGet(Guid clientId, string relative, string etag)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/{clientId}/{relative}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        return _http!.SendAsync(request);
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    // Kestrel's typed Cache-Control header rewrites directive order. Membership is exact.
    // This is not a comparison of file byte totals.
    private static void AssertSameCacheDirectives(string? actual, string expected)
    {
        Assert.NotNull(actual);
        Assert.Equal(CacheDirectives(expected), CacheDirectives(actual));
    }

    private static string[] CacheDirectives(string value)
        => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(directive => directive, StringComparer.Ordinal)
            .ToArray();

    private static string LocalContentType(string path)
        => LocalContentTypes.TryGetContentType(path, out var contentType) ? contentType : "application/octet-stream";

    private static string LocalMissingBody(string relativePath)
        => $"There is no content at {relativePath}";

    private static string BaseHref(string html)
    {
        var marker = "base href=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, html);
        start += marker.Length;
        var end = html.IndexOf('"', start);
        return html[start..end];
    }

    private sealed class DuplexHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly TaskCompletionSource<bool> _appReady;
        private readonly TaskCompletionSource<bool> _emptyReady;
        public string Url { get; }

        private DuplexHost(WebApplication app, string url, TaskCompletionSource<bool> appReady, TaskCompletionSource<bool> emptyReady)
        {
            _app = app;
            Url = url;
            _appReady = appReady;
            _emptyReady = emptyReady;
        }

        public static async Task<DuplexHost> StartAsync(Guid appClient, Guid emptyClient)
        {
            var appReady = ReadySource();
            var emptyReady = ReadySource();
            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
            states[appClient.ToString()] = Seed(appReady);
            states[emptyClient.ToString()] = Seed(emptyReady);

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
            builder.Services.AddSingleton(new RemoteFilesOptions { UseClientCache = true, UseServerCache = false });
            builder.Services.AddSingleton<ServerFileSyncManager>();
            builder.Services.AddSingleton<RemoteFileResolver>();
            builder.Services.AddSingleton<ShutdownService>();
            builder.Services.AddSingleton<RemoteWebViewService>();
            var app = builder.Build();
            app.UseMiddleware<RemoteFilesMiddleware>();
            app.MapGrpcService<RemoteWebViewService>();
            await app.StartAsync();
            return new DuplexHost(app, app.Urls.Single(), appReady, emptyReady);
        }

        public async Task WaitUntilReady()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _appReady.Task.WaitAsync(timeout.Token);
            await _emptyReady.Task.WaitAsync(timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static TaskCompletionSource<bool> ReadySource()
            => new(TaskCreationOptions.RunContinuationsAsynchronously);

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
