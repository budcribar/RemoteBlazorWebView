using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using ServiceFileStream = PeakSWC.RemoteWebView.RemoteStaticFiles.FileStream;

namespace StaticContentCache.Tests;

public class RemoteStaticFileHeaderTests
{
    [Fact]
    public async Task Provider_value_is_the_response_cache_control_header()
    {
        await using var host = await HeaderHost.StartAsync(new FileMetadata
        {
            Length = 5,
            StatusCode = 200,
            ETag = "etag-1",
            LastModified = DateTimeOffset.UnixEpoch.ToUnixTimeSeconds(),
            CacheControl = "public, max-age=86400"
        });

        var response = await host.Client.GetAsync($"/{host.ClientId}/images/catalog/a.png?v=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=86400", Header(response, "Cache-Control"));
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.False(response.Headers.Contains("ETag"));
        Assert.Equal("/images/catalog/a.png?v=2", host.Resolver.LastRequestUri);
        Assert.Equal("image/png", host.Resolver.LastContentType);
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Maui_default_no_store_is_forwarded()
    {
        await using var host = await HeaderHost.StartAsync(new FileMetadata
        {
            Length = 5,
            StatusCode = 200,
            ETag = "etag-1",
            CacheControl = "no-cache, max-age=0, must-revalidate, no-store"
        });

        var response = await host.Client.GetAsync($"/{host.ClientId}/css/app.css");

        // Kestrel rewrites Cache-Control through its typed header, so directive order on the wire
        // is not the string the middleware assigned.
        Assert.Equal("no-store, must-revalidate, no-cache, max-age=0", Header(response, "Cache-Control"));
        Assert.False(response.Headers.Contains("ETag"));
    }

    [Fact]
    public async Task Missing_provider_keeps_the_existing_remote_headers()
    {
        await using var uncached = await HeaderHost.StartAsync(new FileMetadata
        {
            Length = 5,
            StatusCode = 200,
            ETag = "etag-1",
            LastModified = 0
        });
        var plain = await uncached.Client.GetAsync($"/{uncached.ClientId}/css/app.css");
        Assert.Null(Header(plain, "Cache-Control"));
        Assert.False(plain.Headers.Contains("ETag"));
        Assert.False(plain.Content.Headers.Contains("Last-Modified"));

        await using var cached = await HeaderHost.StartAsync(new FileMetadata
        {
            Length = 5,
            StatusCode = 200,
            ETag = "etag-1",
            LastModified = 0
        }, useClientCache: true);
        var withEtag = await cached.Client.GetAsync($"/{cached.ClientId}/css/app.css");
        Assert.Equal("public, max-age=3600", Header(withEtag, "Cache-Control"));
        Assert.Equal("etag-1", Header(withEtag, "ETag"));
        Assert.NotNull(withEtag.Content.Headers.LastModified);
    }

    [Fact]
    public async Task Supplied_header_replaces_the_client_cache_max_age_and_304_keeps_it()
    {
        await using var host = await HeaderHost.StartAsync(new FileMetadata
        {
            Length = 5,
            StatusCode = 200,
            ETag = "etag-1",
            LastModified = 0,
            CacheControl = "public, max-age=86400"
        }, useClientCache: true);

        var first = await host.Client.GetAsync($"/{host.ClientId}/images/catalog/a.png");
        Assert.Equal("public, max-age=86400", Header(first, "Cache-Control"));
        Assert.Equal("etag-1", Header(first, "ETag"));

        var request = new HttpRequestMessage(HttpMethod.Get, $"/{host.ClientId}/images/catalog/a.png");
        request.Headers.TryAddWithoutValidation("If-None-Match", "etag-1");
        var second = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Equal("public, max-age=86400", Header(second, "Cache-Control"));
        Assert.Equal("etag-1", Header(second, "ETag"));
    }

    [Fact]
    public async Task CrLf_cache_control_is_not_emitted()
    {
        await using var host = await HeaderHost.StartAsync(new FileMetadata
        {
            Length = 5,
            StatusCode = 200,
            CacheControl = "public\r\nX-Injected: 1"
        });

        var response = await host.Client.GetAsync($"/{host.ClientId}/css/app.css");

        Assert.Equal("no-store, must-revalidate, no-cache, max-age=0", Header(response, "Cache-Control"));
        Assert.False(response.Headers.Contains("X-Injected"));
    }

    [Fact]
    public async Task Curl_against_kestrel_shows_the_provider_header()
    {
        await using var host = await HeaderHost.StartKestrelAsync(new FileMetadata
        {
            Length = 5,
            StatusCode = 200,
            CacheControl = "public, max-age=86400"
        });

        var psi = new ProcessStartInfo
        {
            FileName = "curl",
            ArgumentList = { "-sD", "-", "-o", "/dev/null", $"{host.Url}/{host.ClientId}/images/catalog/a.png?v=2" },
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("curl did not start");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("HTTP/", stdout, StringComparison.Ordinal);
        Assert.Contains("Cache-Control: public, max-age=86400", stdout, StringComparison.Ordinal);
        Assert.Contains("Content-Type: image/png", stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("X-Injected", stdout, StringComparison.Ordinal);
        Assert.True(string.IsNullOrEmpty(stderr), stderr);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("public\r\nX-Injected: 1")]
    public void Rejected_cache_control_values_become_the_maui_default(string value)
    {
        Assert.Equal(RemoteFilesMiddleware.MauiDefaultCacheControl, RemoteFilesMiddleware.NormalizeCacheControl(value));
    }

    [Fact]
    public void Empty_cache_control_stays_unset_and_a_normal_value_is_unchanged()
    {
        Assert.Null(RemoteFilesMiddleware.NormalizeCacheControl(null));
        Assert.Null(RemoteFilesMiddleware.NormalizeCacheControl(""));
        Assert.Equal("public, max-age=86400", RemoteFilesMiddleware.NormalizeCacheControl("public, max-age=86400"));
    }

    [Fact]
    public void Oversized_cache_control_becomes_the_maui_default()
    {
        var oversized = new string('a', RemoteFilesMiddleware.MaxCacheControlLength + 1);
        Assert.Equal(RemoteFilesMiddleware.MauiDefaultCacheControl, RemoteFilesMiddleware.NormalizeCacheControl(oversized));
        Assert.Equal(new string('a', RemoteFilesMiddleware.MaxCacheControlLength), RemoteFilesMiddleware.NormalizeCacheControl(new string('a', RemoteFilesMiddleware.MaxCacheControlLength)));
    }

    [Theory]
    [InlineData("images/catalog/a.png", "image/png")]
    [InlineData("app.js", "text/javascript")]
    [InlineData("app.css", "text/css")]
    [InlineData("dotnet.wasm", "application/wasm")]
    [InlineData("appsettings.json", "application/json")]
    [InlineData("file.unknownext", "application/octet-stream")]
    public async Task Content_type_is_passed_for_each_extension(string relativePath, string contentType)
    {
        await using var host = await HeaderHost.StartAsync(OkMetadata("etag-1"));
        var response = await host.Client.GetAsync($"/{host.ClientId}/{relativePath}?v=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(contentType, host.Resolver.ContentTypes[relativePath]);
        Assert.Equal("/" + relativePath + "?v=2", host.Resolver.RequestUris[relativePath]);
    }

    [Fact]
    public async Task Encoded_path_and_query_are_forwarded_without_the_client_id()
    {
        await using var host = await HeaderHost.StartAsync(OkMetadata("etag-1"));
        var response = await host.Client.GetAsync($"/{host.ClientId}/images/my%20file.png?name=a%20b&v=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("/images/my file.png?name=a%20b&v=2", host.Resolver.RequestUris["images/my file.png"]);
        Assert.Equal("image/png", host.Resolver.ContentTypes["images/my file.png"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Provider_header_is_on_200_for_both_client_cache_settings(bool useClientCache)
    {
        await using var host = await HeaderHost.StartAsync(new FileMetadata
        {
            Length = 5,
            StatusCode = 200,
            ETag = "etag-1",
            LastModified = 0,
            CacheControl = "public, max-age=86400"
        }, useClientCache);

        var response = await host.Client.GetAsync($"/{host.ClientId}/app.css");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=86400", Header(response, "Cache-Control"));
        Assert.Equal(useClientCache, response.Headers.Contains("ETag"));
        Assert.Equal(useClientCache, response.Content.Headers.Contains("Last-Modified"));
    }

    [Fact]
    public async Task Head_post_and_put_do_not_receive_the_static_cache_header()
    {
        await using var host = await HeaderHost.StartAsync(OkMetadata("etag-1", "public, max-age=86400"));

        foreach (var method in new[] { HttpMethod.Head, HttpMethod.Post, HttpMethod.Put })
        {
            var response = await host.Client.SendAsync(new HttpRequestMessage(method, $"/{host.ClientId}/app.css"));
            Assert.Equal((HttpStatusCode)418, response.StatusCode);
            Assert.Null(Header(response, "Cache-Control"));
        }
    }

    [Fact]
    public async Task Range_request_still_returns_the_full_body_and_the_provider_header()
    {
        await using var host = await HeaderHost.StartAsync(OkMetadata("etag-1", "public, max-age=86400"));
        var request = new HttpRequestMessage(HttpMethod.Get, $"/{host.ClientId}/app.css");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=86400", Header(response, "Cache-Control"));
        Assert.Equal("hello", await response.Content.ReadAsStringAsync());
        Assert.Null(Header(response, "Content-Range"));
    }

    [Fact]
    public async Task Concurrent_files_keep_their_own_provider_results()
    {
        await using var host = await HeaderHost.StartAsync(OkMetadata("etag-1"), cacheByPath: path => path.EndsWith(".png", StringComparison.Ordinal) ? "public, max-age=86400" : "public, max-age=60");

        var png = host.Client.GetAsync($"/{host.ClientId}/images/a.png");
        var css = host.Client.GetAsync($"/{host.ClientId}/css/app.css");
        await Task.WhenAll(png, css);

        Assert.Equal("public, max-age=86400", Header(await png, "Cache-Control"));
        Assert.Equal("public, max-age=60", Header(await css, "Cache-Control"));
        Assert.Equal("image/png", host.Resolver.ContentTypes["images/a.png"]);
        Assert.Equal("text/css", host.Resolver.ContentTypes["css/app.css"]);
    }

    private static FileMetadata OkMetadata(string etag, string? cacheControl = null) => new()
    {
        Length = 5,
        StatusCode = 200,
        ETag = etag,
        LastModified = 0,
        CacheControl = cacheControl ?? ""
    };

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private sealed class HeaderHost : IAsyncDisposable
    {
        private readonly IHost? _host;
        private readonly WebApplication? _app;
        public HttpClient Client { get; }
        public Guid ClientId { get; }
        public RecordingResolver Resolver { get; }
        public string? Url { get; }

        private HeaderHost(IHost? host, WebApplication? app, HttpClient client, Guid clientId, RecordingResolver resolver, string? url)
        {
            _host = host;
            _app = app;
            Client = client;
            ClientId = clientId;
            Resolver = resolver;
            Url = url;
        }

        public static Task<HeaderHost> StartAsync(FileMetadata metadata, bool useClientCache = false, Func<string, string?>? cacheByPath = null)
            => StartCoreAsync(metadata, useClientCache, kestrel: false, cacheByPath);

        public static Task<HeaderHost> StartKestrelAsync(FileMetadata metadata)
            => StartCoreAsync(metadata, useClientCache: false, kestrel: true);

        private static async Task<HeaderHost> StartCoreAsync(FileMetadata metadata, bool useClientCache, bool kestrel, Func<string, string?>? cacheByPath = null)
        {
            var clientId = Guid.NewGuid();
            var resolver = new RecordingResolver(metadata) { CacheByPath = cacheByPath };
            var states = ReadyStates(clientId);

            if (!kestrel)
            {
                var host = await new HostBuilder()
                    .ConfigureWebHost(web =>
                    {
                        web.UseTestServer();
                        web.ConfigureServices(services => AddServices(services, resolver, states, useClientCache));
                        web.Configure(app =>
                        {
                            app.UseMiddleware<RemoteFilesMiddleware>();
                            app.Run(context =>
                            {
                                context.Response.StatusCode = StatusCodes.Status418ImATeapot;
                                return Task.CompletedTask;
                            });
                        });
                    })
                    .StartAsync();
                return new HeaderHost(host, null, host.GetTestClient(), clientId, resolver, null);
            }

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            AddServices(builder.Services, resolver, states, useClientCache);
            var app = builder.Build();
            app.UseMiddleware<RemoteFilesMiddleware>();
            app.Run(context =>
            {
                context.Response.StatusCode = StatusCodes.Status418ImATeapot;
                return Task.CompletedTask;
            });
            await app.StartAsync();
            return new HeaderHost(null, app, new HttpClient(), clientId, resolver, app.Urls.Single());
        }

        private static void AddServices(IServiceCollection services, RecordingResolver resolver, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> states, bool useClientCache)
        {
            services.AddLogging();
            services.AddMemoryCache();
            services.AddSingleton(new RemoteFilesOptions { UseClientCache = useClientCache, UseServerCache = false });
            services.AddSingleton(new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance));
            services.AddSingleton<RemoteFileResolver>(resolver);
            services.AddSingleton(states);
        }

        private static ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> ReadyStates(Guid clientId)
        {
            var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
            state.FileManagerReady.SetResult(true);
            var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            ready.SetResult(state);
            return new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>
            {
                [clientId.ToString()] = ready
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
                await _host.StopAsync();
            _host?.Dispose();
            if (_app is not null)
            {
                await _app.StopAsync();
                await _app.DisposeAsync();
            }
            Client.Dispose();
            Resolver.DisposeManager();
        }
    }

    private sealed class RecordingResolver : RemoteFileResolver
    {
        private readonly ServerFileSyncManager _manager;
        private readonly FileMetadata _metadata;

        public RecordingResolver(FileMetadata metadata)
            : this(metadata, new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance))
        {
        }

        private RecordingResolver(FileMetadata metadata, ServerFileSyncManager manager)
            : base(NullLogger<RemoteFileResolver>.Instance, manager)
        {
            _metadata = metadata;
            _manager = manager;
        }

        public string? LastRequestUri { get; private set; }
        public string? LastContentType { get; private set; }
        public Func<string, string?>? CacheByPath { get; set; }
        public ConcurrentDictionary<string, string?> RequestUris { get; } = new();
        public ConcurrentDictionary<string, string?> ContentTypes { get; } = new();

        public override Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subpath, string? requestUri = null, string? contentType = null)
        {
            LastRequestUri = requestUri;
            LastContentType = contentType;
            RequestUris[subpath] = requestUri;
            ContentTypes[subpath] = contentType;
            var metadata = _metadata.Clone();
            if (CacheByPath is not null)
                metadata.CacheControl = CacheByPath(subpath) ?? "";
            return Task.FromResult(metadata);
        }

        public override Task<ServiceFileStream> GetFileStreamAsync(string clientId, string subpath)
            => Task.FromResult(new ServiceFileStream { Stream = new MemoryStream("hello"u8.ToArray()) });

        public void DisposeManager() => _manager.Dispose();
    }
}
