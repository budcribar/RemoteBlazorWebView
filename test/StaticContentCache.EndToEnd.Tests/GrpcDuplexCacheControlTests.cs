extern alias client;

using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;
using ClientSync = client::PeakSWC.RemoteWebView.ClientFileSyncManager;
using ClientRequest = client::PeakSWC.RemoteWebView.BlazorWebViewStaticContentRequest;

namespace StaticContentCache.EndToEnd.Tests;

public class GrpcDuplexCacheControlTests
{
    [Fact]
    public async Task Real_grpc_duplex_carries_provider_results_to_the_http_response()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var clientId = Guid.NewGuid();
        var seen = new ConcurrentDictionary<string, ClientRequest>();
        var files = new MemoryFileProvider(new Dictionary<string, byte[]>
        {
            ["images/a.png"] = "png-bytes"u8.ToArray(),
            ["css/app.css"] = "css-bytes"u8.ToArray(),
            ["scripts/app.js"] = "js-bytes"u8.ToArray()
        });

        await using var host = await DuplexHost.StartAsync(clientId, useClientCache: true);
        using var channel = GrpcChannel.ForAddress(host.Url, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                UseCookies = false
            }
        });
        var ipc = new client::PeakSWC.RemoteWebView.WebViewIPC.WebViewIPCClient(channel);
        Exception? clientError = null;
        var sync = new ClientSync(
            ipc,
            clientId,
            "index.html",
            files,
            error => clientError = error,
            NullLogger.Instance,
            request =>
            {
                seen[request.Uri.AbsolutePath] = request;
                if (request.Uri.AbsolutePath.EndsWith(".png", StringComparison.Ordinal) && request.ContentType == "image/png")
                    return request.Uri.Query == "?v=2" ? "public, max-age=86400" : "public, max-age=1";
                if (request.Uri.AbsolutePath.EndsWith(".css", StringComparison.Ordinal))
                    return null;
                return "public, max-age=30";
            });
        sync.HandleServerRequests(CancellationToken.None);

        using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            while (!host.FileManagerReady.Task.IsCompleted)
            {
                if (clientError is not null)
                    throw new InvalidOperationException("Desktop file client failed before the server marked the file manager ready. " + string.Join(" | ", host.Requests), clientError);
                await Task.Delay(50, readyTimeout.Token);
            }
        }
        catch (OperationCanceledException) when (!host.FileManagerReady.Task.IsCompleted)
        {
            throw new TimeoutException($"File manager was not ready. Url={host.Url}. Requests=[{string.Join(" | ", host.Requests)}]. ClientError={clientError}");
        }
        Assert.True(await host.FileManagerReady.Task);
        Assert.Null(clientError);

        using var http = new HttpClient(new SocketsHttpHandler())
        {
            BaseAddress = new Uri(host.Url),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        var pngTask = http.GetAsync($"/{clientId}/images/a.png?v=2");
        var cssTask = http.GetAsync($"/{clientId}/css/app.css");
        var jsTask = http.GetAsync($"/{clientId}/scripts/app.js?name=a%20b");
        await Task.WhenAll(pngTask, cssTask, jsTask);
        var png = await pngTask;
        var css = await cssTask;
        var js = await jsTask;

        Assert.Equal(HttpStatusCode.OK, png.StatusCode);
        Assert.Equal("public, max-age=86400", Header(png, "Cache-Control"));
        Assert.Equal("image/png", png.Content.Headers.ContentType?.MediaType);
        Assert.Equal("png-bytes", await png.Content.ReadAsStringAsync());
        Assert.Equal("?v=2", seen["/images/a.png"].Uri.Query);
        Assert.False(string.IsNullOrEmpty(Header(png, "ETag")));

        Assert.Equal("no-store, must-revalidate, no-cache, max-age=0", Header(css, "Cache-Control"));
        Assert.Equal("text/css", css.Content.Headers.ContentType?.MediaType);
        Assert.Equal("css-bytes", await css.Content.ReadAsStringAsync());
        Assert.Equal("text/css", seen["/css/app.css"].ContentType);

        Assert.Equal("public, max-age=30", Header(js, "Cache-Control"));
        Assert.Equal("text/javascript", seen["/scripts/app.js"].ContentType);
        Assert.Equal("?name=a%20b", seen["/scripts/app.js"].Uri.Query);

        var etag = Header(png, "ETag");
        var conditional = new HttpRequestMessage(HttpMethod.Get, $"/{clientId}/images/a.png?v=2")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        // HttpRequestMessage defaults to HTTP/1.1. This host is HTTP/2-only, so the conditional GET has to say so.
        conditional.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var notModified = await http.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Equal("public, max-age=86400", Header(notModified, "Cache-Control"));
        Assert.Equal(etag, Header(notModified, "ETag"));

        await sync.CloseAsync();
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private sealed class DuplexHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string Url { get; }
        public TaskCompletionSource<bool> FileManagerReady { get; }
        public ConcurrentQueue<string> Requests { get; }

        private DuplexHost(WebApplication app, string url, TaskCompletionSource<bool> ready, ConcurrentQueue<string> requests)
        {
            _app = app;
            Url = url;
            FileManagerReady = ready;
            Requests = requests;
        }

        public static async Task<DuplexHost> StartAsync(Guid clientId, bool useClientCache)
        {
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
            var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
            var stateReady = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            stateReady.SetResult(state);
            states[clientId.ToString()] = stateReady;
            state.FileManagerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = state.FileManagerReady.Task.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                    ready.TrySetResult(task.Result);
                else if (task.Exception is not null)
                    ready.TrySetException(task.Exception);
                else
                    ready.TrySetException(new InvalidOperationException("File manager was not ready."));
            }, TaskScheduler.Default);

            var requests = new ConcurrentQueue<string>();
            var builder = WebApplication.CreateBuilder();
            // .NET 11 Kestrel serves only HTTP/1.1 on a cleartext Http1AndHttp2 endpoint
            // ("HTTP/2 requires TLS application protocol negotiation"). An HTTP/2-only
            // endpoint accepts prior-knowledge h2c, which is what the gRPC client uses.
            // The file HttpClient below uses the same HTTP/2 connection style.
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
            builder.Services.AddSingleton(new RemoteFilesOptions { UseClientCache = useClientCache, UseServerCache = false });
            builder.Services.AddSingleton<ServerFileSyncManager>();
            builder.Services.AddSingleton<RemoteFileResolver>();
            builder.Services.AddSingleton<ShutdownService>();
            builder.Services.AddSingleton<RemoteWebViewService>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                requests.Enqueue($"{context.Request.Method} {context.Request.Path}{context.Request.QueryString} {context.Request.Protocol}");
                await next();
                requests.Enqueue($"-> {context.Response.StatusCode} {context.Request.Path}");
            });
            // Files are matched here. POST (gRPC) is forwarded. Registering this before the
            // gRPC endpoint keeps a GET from depending on unmatched-route fallthrough.
            app.UseMiddleware<RemoteFilesMiddleware>();
            app.MapGrpcService<RemoteWebViewService>();
            await app.StartAsync();
            return new DuplexHost(app, app.Urls.Single(), ready, requests);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private sealed class MemoryFileProvider(Dictionary<string, byte[]> files) : Microsoft.Extensions.FileProviders.IFileProvider
    {
        public Microsoft.Extensions.FileProviders.IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
        public Microsoft.Extensions.FileProviders.IFileInfo GetFileInfo(string subpath)
        {
            var key = subpath.TrimStart('/');
            return files.TryGetValue(key, out var bytes)
                ? new MemoryFile(key, bytes)
                : new NotFoundFileInfo(key);
        }
    }

    private sealed class MemoryFile(string path, byte[] bytes) : Microsoft.Extensions.FileProviders.IFileInfo
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
