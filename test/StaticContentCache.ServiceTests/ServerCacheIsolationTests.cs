using System.Collections.Concurrent;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using ServiceFileStream = PeakSWC.RemoteWebView.FileStream;

namespace StaticContentCache.Tests;

// net9 has no per-file provider Cache-Control. These tests cover the shared
// server-cache key: bodies stay with the client that owns them.
public class ServerCacheIsolationTests
{
    [Fact]
    public async Task Same_etag_does_not_serve_another_clients_bytes()
    {
        await using var host = await TwoClientCache.StartAsync();
        var clientA = host.Add("css/app.css", etag: "same-etag", body: "client-a-css");
        var clientB = host.Add("css/app.css", etag: "same-etag", body: "client-b-css");

        var first = await host.GetAsync(clientA, "css/app.css");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal("client-a-css", await first.Content.ReadAsStringAsync());

        var second = await host.GetAsync(clientB, "css/app.css");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("client-b-css", await second.Content.ReadAsStringAsync());
        Assert.Equal(1, host.StreamReads(clientB, "css/app.css"));
    }

    [Fact]
    public async Task Different_etag_refreshes_the_writer_and_does_not_evict_the_other_client()
    {
        await using var host = await TwoClientCache.StartAsync();
        var clientA = host.Add("index.html", etag: "etag-a", body: "page-a");
        var clientB = host.Add("index.html", etag: "etag-b", body: "page-b");

        Assert.Equal("page-a", await (await host.GetAsync(clientA, "index.html")).Content.ReadAsStringAsync());
        Assert.Equal("page-b", await (await host.GetAsync(clientB, "index.html")).Content.ReadAsStringAsync());

        var again = await host.GetAsync(clientA, "index.html");
        Assert.Equal("page-a", await again.Content.ReadAsStringAsync());
        Assert.Equal(1, host.StreamReads(clientA, "index.html"));
    }

    [Fact]
    public async Task Changed_file_replaces_the_cached_body_and_cache_control()
    {
        await using var host = await TwoClientCache.StartAsync(useClientCache: true);
        var client = host.Add("css/app.css", etag: "v1", body: "old");

        var first = await host.GetAsync(client, "css/app.css");
        Assert.Equal("old", await first.Content.ReadAsStringAsync());
        Assert.Contains("max-age=3600", Header(first, "Cache-Control"), StringComparison.Ordinal);

        host.Replace(client, "css/app.css", etag: "v2", body: "new");

        var updated = await host.GetAsync(client, "css/app.css");
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("new", await updated.Content.ReadAsStringAsync());
        Assert.Contains("max-age=3600", Header(updated, "Cache-Control"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conditional_get_uses_the_requesting_clients_cache_control()
    {
        await using var host = await TwoClientCache.StartAsync(useClientCache: true);
        var clientA = host.Add("css/app.css", etag: "etag-a", body: "page-a");
        var clientB = host.Add("css/app.css", etag: "etag-b", body: "page-b");

        Assert.Equal("page-a", await (await host.GetAsync(clientA, "css/app.css")).Content.ReadAsStringAsync());

        var request = new HttpRequestMessage(HttpMethod.Get, $"/{clientB}/css/app.css");
        request.Headers.TryAddWithoutValidation("If-None-Match", "etag-b");
        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Equal("", await response.Content.ReadAsStringAsync());

        var full = await host.GetAsync(clientB, "css/app.css");
        Assert.Equal("page-b", await full.Content.ReadAsStringAsync());
        Assert.Equal(1, host.StreamReads(clientB, "css/app.css"));
    }

    private static string? Header(HttpResponseMessage response, string name)
        => response.Headers.TryGetValues(name, out var values) ? values.Single() : null;

    private sealed class TwoClientCache : IAsyncDisposable
    {
        private readonly IHost _host;
        private readonly PerClientResolver _resolver;
        public HttpClient Client { get; }

        private TwoClientCache(IHost host, HttpClient client, PerClientResolver resolver)
        {
            _host = host;
            Client = client;
            _resolver = resolver;
        }

        public static async Task<TwoClientCache> StartAsync(bool useClientCache = false)
        {
            var resolver = new PerClientResolver();
            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
            var host = await new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddLogging();
                        services.AddMemoryCache();
                        services.AddSingleton(new RemoteFilesOptions { UseClientCache = useClientCache, UseServerCache = true });
                        services.AddSingleton(resolver.Manager);
                        services.AddSingleton<RemoteFileResolver>(resolver);
                        services.AddSingleton(states);
                    });
                    web.Configure(app => app.UseMiddleware<RemoteFilesMiddleware>());
                })
                .StartAsync();
            return new TwoClientCache(host, host.GetTestClient(), resolver) { States = states };
        }

        private ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> States { get; set; } = new();

        public Guid Add(string path, string etag, string body)
        {
            var id = Guid.NewGuid();
            var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
            state.FileManagerReady.SetResult(true);
            var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            ready.SetResult(state);
            States[id.ToString()] = ready;
            _resolver.Put(id, path, etag, body);
            return id;
        }

        public void Replace(Guid id, string path, string etag, string body)
            => _resolver.Put(id, path, etag, body);

        public int StreamReads(Guid id, string path) => _resolver.Reads(id, path);

        public Task<HttpResponseMessage> GetAsync(Guid id, string path)
            => Client.GetAsync($"/{id}/{path}");

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
            Client.Dispose();
            _resolver.Manager.Dispose();
        }
    }

    private sealed class PerClientResolver : RemoteFileResolver
    {
        private readonly ConcurrentDictionary<(string Client, string Path), Entry> _files = new();
        public ServerFileSyncManager Manager { get; }

        public PerClientResolver()
            : this(new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance))
        {
        }

        private PerClientResolver(ServerFileSyncManager manager)
            : base(NullLogger<RemoteFileResolver>.Instance, manager)
        {
            Manager = manager;
        }

        public void Put(Guid id, string path, string etag, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            _files[(id.ToString(), path)] = new Entry(etag, bytes);
        }

        public int Reads(Guid id, string path)
            => _files.TryGetValue((id.ToString(), path), out var entry) ? entry.Reads : 0;

        public override Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subpath)
        {
            var entry = _files[(clientId, subpath)];
            return Task.FromResult(new FileMetadata
            {
                Length = entry.Bytes.Length,
                StatusCode = StatusCodes.Status200OK,
                ETag = entry.ETag,
                LastModified = 0
            });
        }

        public override Task<ServiceFileStream> GetFileStreamAsync(string clientId, string subpath)
        {
            var entry = _files[(clientId, subpath)];
            Interlocked.Increment(ref entry.Reads);
            return Task.FromResult(new ServiceFileStream { Stream = new MemoryStream(entry.Bytes, writable: false) });
        }

        private sealed class Entry(string etag, byte[] bytes)
        {
            public string ETag { get; set; } = etag;
            public byte[] Bytes { get; set; } = bytes;
            public int Reads;
        }
    }
}
