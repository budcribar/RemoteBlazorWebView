extern alias client;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading.Channels;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;
#if !NET9_0
using PeakSWC.RemoteWebView.RemoteStaticFiles;
#endif
using PeakSWC.RemoteWebView.Services;
using ClientSync = client::PeakSWC.RemoteWebView.ClientFileSyncManager;

// In-process counts for the perf-regression workflow. The projects under
// src/Benchmarks measure a running RemoteWebViewService.exe or a fixed LAN host,
// so their timings move with the machine. This harness records request counts,
// payload bytes, and allocations for the same scenarios on whatever build it is
// compiled against. One JSON object is written to stdout.

AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var results = new List<ScenarioResult>();
await RunWarmup();
results.Add(await MeasureStatic("static-fetch-no-cache", useClientCache: false, useProvider: false));
results.Add(await MeasureStatic("static-fetch-client-cache", useClientCache: true, useProvider: false));
if (SyncConstructorHasProvider())
    results.Add(await MeasureStatic("static-fetch-with-provider", useClientCache: true, useProvider: true));
results.Add(await MeasureReconnect());

Console.WriteLine(JsonSerializer.Serialize(new { scenarios = results }, JsonOptions()));
return;

static async Task RunWarmup()
{
    var sample = await MeasureStatic("warmup", useClientCache: false, useProvider: false);
    _ = sample;
    _ = await MeasureReconnect();
}

static async Task<ScenarioResult> MeasureStatic(string name, bool useClientCache, bool useProvider)
{
    var files = SampleFiles();
    var provider = new CountingFileProvider(files);
    await using var host = await DuplexHost.StartAsync(useClientCache);
    var clientId = Guid.NewGuid();
    var state = host.Prepare(clientId);
    Exception? clientError = null;
    var sync = CreateSync(host, clientId, provider, useProvider, error => clientError = error);
    sync.HandleServerRequests(CancellationToken.None);
    await WaitUntilReady(state, clientError);

    // One unmeasured pass so the measured pass is not the first JIT of the fetch path.
    await FetchAll(host, clientId, conditional: false);

    provider.Reset();
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
    var clock = Stopwatch.StartNew();
    var first = await FetchAll(host, clientId, conditional: false);
    var second = await FetchAll(host, clientId, conditional: useClientCache);
    clock.Stop();
    var allocated = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;

    // Close races the in-flight duplex read. The counts above are already final.
    try
    {
        await sync.CloseAsync();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("close failed during " + name + ": " + ex.Message);
    }
    if (clientError is not null)
        throw new InvalidOperationException("File client failed during " + name, clientError);

    return new ScenarioResult(name, new Dictionary<string, double>
    {
        ["elapsedMs"] = clock.Elapsed.TotalMilliseconds,
        ["allocatedBytes"] = allocated,
        ["httpGets"] = first.Gets + second.Gets,
        ["http304"] = first.NotModified + second.NotModified,
        ["responseBodyBytes"] = first.BodyBytes + second.BodyBytes,
        ["cacheControlBytes"] = first.CacheControlBytes + second.CacheControlBytes,
        ["grpcStreams"] = host.PostsObserved,
        ["fileInfoReads"] = provider.InfoReads,
        ["streamOpens"] = provider.StreamOpens,
        ["streamBytes"] = provider.StreamBytes
    });
}

static async Task<ScenarioResult> MeasureReconnect()
{
    await using var host = await DuplexHost.StartAsync(useClientCache: false);
    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: false);
    var clock = Stopwatch.StartNew();
    await ConnectAndClose(host);
    await ConnectAndClose(host);
    clock.Stop();
    var allocated = GC.GetTotalAllocatedBytes(precise: false) - allocatedBefore;

    return new ScenarioResult("connect-reconnect", new Dictionary<string, double>
    {
        ["elapsedMs"] = clock.Elapsed.TotalMilliseconds,
        ["allocatedBytes"] = allocated,
        ["grpcStreams"] = host.PostsObserved
    });
}

static async Task ConnectAndClose(DuplexHost host)
{
    var clientId = Guid.NewGuid();
    var state = host.Prepare(clientId);
    Exception? clientError = null;
    var sync = CreateSync(host, clientId, new CountingFileProvider(SampleFiles()), useProvider: false, error => clientError = error);
    sync.HandleServerRequests(CancellationToken.None);
    await WaitUntilReady(state, clientError);
    await sync.CloseAsync();
}

static async Task WaitUntilReady(ServiceState state, Exception? clientError)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    while (!state.FileManagerReady.Task.IsCompleted)
    {
        if (clientError is not null)
            throw new InvalidOperationException("Desktop file client failed before the file manager was ready.", clientError);
        await Task.Delay(20, timeout.Token);
    }
    if (!await state.FileManagerReady.Task)
        throw new InvalidOperationException("File manager was not ready.");
}

static async Task<FetchTotals> FetchAll(DuplexHost host, Guid clientId, bool conditional)
{
    using var http = new HttpClient(new SocketsHttpHandler())
    {
        BaseAddress = new Uri(host.Url),
        DefaultRequestVersion = HttpVersion.Version20,
        DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact
    };
    var totals = new FetchTotals();
    foreach (var path in new[] { "images/a.png", "css/app.css", "scripts/app.js" })
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/{clientId}/{path}")
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        if (conditional && host.Etags.TryGetValue(path, out var etag))
            request.Headers.TryAddWithoutValidation("If-None-Match", etag);

        var response = await http.SendAsync(request);
        totals.Gets++;
        if ((int)response.StatusCode == 304)
            totals.NotModified++;
        var body = await response.Content.ReadAsByteArrayAsync();
        totals.BodyBytes += body.LongLength;
        if (response.Headers.TryGetValues("Cache-Control", out var cache))
            totals.CacheControlBytes += cache.Sum(value => value.Length);
        if (response.Headers.TryGetValues("ETag", out var tags))
            host.Etags[path] = tags.First();
    }
    return totals;
}

static ClientSync CreateSync(DuplexHost host, Guid clientId, Microsoft.Extensions.FileProviders.IFileProvider files, bool useProvider, Action<Exception> onError)
{
    var channel = GrpcChannel.ForAddress(host.Url, new GrpcChannelOptions
    {
        HttpHandler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            UseCookies = false
        }
    });
    host.Track(channel);
    var ipc = new client::PeakSWC.RemoteWebView.WebViewIPC.WebViewIPCClient(channel);
    var ctor = typeof(ClientSync).GetConstructors().Single();
    var arguments = new List<object?>
    {
        ipc,
        clientId,
        "index.html",
        files,
        onError,
        NullLogger.Instance
    };
    if (ctor.GetParameters().Length == 7)
        arguments.Add(useProvider ? BuildProvider(ctor.GetParameters()[6].ParameterType) : null);
    return (ClientSync)ctor.Invoke(arguments.ToArray())!;
}

static bool SyncConstructorHasProvider()
    => typeof(ClientSync).GetConstructors().Single().GetParameters().Length == 7;

static Delegate BuildProvider(Type funcType)
{
    var requestType = funcType.GetGenericArguments()[0];
    var parameter = Expression.Parameter(requestType, "request");
    return Expression.Lambda(funcType, Expression.Constant("public, max-age=86400"), parameter).Compile();
}

static Dictionary<string, byte[]> SampleFiles() => new()
{
    ["images/a.png"] = Enumerable.Repeat((byte)'A', 128).ToArray(),
    ["css/app.css"] = Enumerable.Repeat((byte)'B', 64).ToArray(),
    ["scripts/app.js"] = Enumerable.Repeat((byte)'C', 256).ToArray()
};

static JsonSerializerOptions JsonOptions() => new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

sealed class FetchTotals
{
    public int Gets;
    public int NotModified;
    public long BodyBytes;
    public long CacheControlBytes;
}

sealed record ScenarioResult(string Name, Dictionary<string, double> Metrics);

sealed class DuplexHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> _states;
    private readonly List<GrpcChannel> _channels = [];

    public string Url { get; }
    public Dictionary<string, string> Etags { get; } = new(StringComparer.Ordinal);
    public int PostsObserved => _box is null ? 0 : _box.Value;

    private DuplexHost(WebApplication app, string url, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> states)
    {
        _app = app;
        Url = url;
        _states = states;
    }

    public static async Task<DuplexHost> StartAsync(bool useClientCache)
    {
        var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var posts = new StrongBox();
        var builder = WebApplication.CreateBuilder();
        // Console logging writes in front of the JSON line and breaks the compare step.
        builder.Logging.ClearProviders();
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
            if (HttpMethods.IsPost(context.Request.Method))
                Interlocked.Increment(ref posts.Value);
            await next();
        });
        app.UseMiddleware<RemoteFilesMiddleware>();
        app.MapGrpcService<RemoteWebViewService>();
        await app.StartAsync();
        var host = new DuplexHost(app, app.Urls.Single(), states) { _box = posts };
        return host;
    }

    private StrongBox? _box;

    public ServiceState Prepare(Guid clientId)
    {
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: false);
        var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.SetResult(state);
        _states[clientId.ToString()] = ready;
        return state;
    }

    public void Track(GrpcChannel channel) => _channels.Add(channel);

    public async ValueTask DisposeAsync()
    {
        foreach (var channel in _channels)
            channel.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    sealed class StrongBox
    {
        public int Value;
    }
}

sealed class CountingFileProvider(Dictionary<string, byte[]> files) : Microsoft.Extensions.FileProviders.IFileProvider
{
    public int InfoReads;
    public int StreamOpens;
    public long StreamBytes;

    public void Reset()
    {
        InfoReads = 0;
        StreamOpens = 0;
        StreamBytes = 0;
    }

    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
    public IFileInfo GetFileInfo(string subpath)
    {
        Interlocked.Increment(ref InfoReads);
        var key = subpath.TrimStart('/');
        return files.TryGetValue(key, out var bytes)
            ? new CountingFile(key, bytes, this)
            : new NotFoundFileInfo(key);
    }

    public void CountOpen() => Interlocked.Increment(ref StreamOpens);
    public void CountBytes(int count) => Interlocked.Add(ref StreamBytes, count);
}

sealed class CountingFile(string path, byte[] bytes, CountingFileProvider owner) : IFileInfo
{
    public bool Exists => true;
    public long Length => bytes.Length;
    public string? PhysicalPath => null;
    public string Name => Path.GetFileName(path);
    public DateTimeOffset LastModified { get; } = DateTimeOffset.UnixEpoch;
    public bool IsDirectory => false;
    public Stream CreateReadStream()
    {
        owner.CountOpen();
        return new CountingStream(new MemoryStream(bytes, writable: false), owner);
    }
}

sealed class CountingStream(Stream inner, CountingFileProvider owner) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        owner.CountBytes(read);
        return read;
    }
    public override int Read(Span<byte> buffer)
    {
        var read = inner.Read(buffer);
        owner.CountBytes(read);
        return read;
    }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await inner.ReadAsync(buffer, cancellationToken);
        owner.CountBytes(read);
        return read;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }
}
