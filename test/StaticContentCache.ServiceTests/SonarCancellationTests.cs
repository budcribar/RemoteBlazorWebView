using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Text;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;
using RemoteFileStream = PeakSWC.RemoteWebView.RemoteStaticFiles.FileStream;

namespace StaticContentCache.Tests;

public class SonarCancellationTests
{
    [Fact]
    public async Task Aborted_file_read_stops_and_does_not_log_an_error()
    {
        await using var host = await AbortHost.StartAsync();
        using var requestAbort = new CancellationTokenSource();
        var pending = host.Client.GetAsync($"/{host.ClientId}/app.css", requestAbort.Token);
        await host.Stream.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        requestAbort.Cancel();
        var finished = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(pending, finished);
        Assert.False(host.Stream.UnblockedByCleanup);
        Assert.Empty(host.Errors);
        host.Cleanup.Cancel();
    }

    [Fact]
    public async Task Cancelled_file_watch_passes_the_token_to_the_chunk_write()
    {
        var path = Path.Combine(Path.GetTempPath(), "rbw-watch-" + Guid.NewGuid().ToString("n"));
        await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4 });
        try
        {
            var writer = new TokenWriter<WatchFileResponse>();
            var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            var method = typeof(FileWatcherService).GetMethod("StreamFileInChunks", BindingFlags.Instance | BindingFlags.NonPublic);
            using var cts = new CancellationTokenSource();
            var streaming = (Task)method!.Invoke(service, new object[] { path, writer, cts.Token })!;
            await writer.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(writer.SawCancelableToken);
            await streaming.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Cancelled_ping_write_observes_the_linked_token()
    {
        var id = Guid.NewGuid().ToString();
        var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
        var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.SetResult(state);
        states[id] = ready;
        var channels = new ConcurrentDictionary<string, System.Threading.Channels.Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, states);
        var files = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, states, channels, shutdown, files);
        var writer = new BlockingPingWriter();
        using var callCancelled = new CancellationTokenSource();
        var reader = new OnePingReader(id);
        var ping = service.Ping(reader, writer, new CallContext(callCancelled.Token));

        await writer.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        callCancelled.Cancel();
        var stopped = await Task.WhenAny(writer.WriteStopped.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(writer.WriteStopped.Task, stopped);
        await state.PingTask!.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(state.PingTask.IsFaulted);
        reader.Release();
        await ping.WaitAsync(TimeSpan.FromSeconds(2));
        files.Dispose();
    }

    [Fact]
    public async Task Disposing_the_file_sync_manager_cancels_an_open_write()
    {
        var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var clientId = Guid.NewGuid().ToString();
        manager.RegisterClient(clientId, "index.html");
        var writer = new BlockingFileWriter();
        manager.AssociateResponseStream(clientId, writer);
        var requesting = manager.RequestFileDataAsync(clientId, "app.css", NullLogger<RemoteFileResolver>.Instance);
        await writer.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposing = Task.Run(manager.Dispose);
        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        var stopped = await Task.WhenAny(writer.WriteStopped.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        writer.Release();
        await requesting.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(writer.UsedToken);
        Assert.Same(writer.WriteStopped.Task, stopped);
    }

    [Fact]
    public async Task Wasm_and_octet_stream_responses_are_gzip_compressed()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var payload = Encoding.UTF8.GetBytes(new string('w', 4096));
        using var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services => new Startup(config).ConfigureServices(services));
                web.Configure(app =>
                {
                    app.UseResponseCompression();
                    app.Run(async context =>
                    {
                        var wasm = context.Request.Path.Value == "/app.wasm";
                        context.Response.ContentType = wasm ? "application/wasm" : "application/octet-stream";
                        await context.Response.Body.WriteAsync(payload);
                    });
                });
            })
            .StartAsync();
        var client = host.GetTestClient();
        foreach (var path in new[] { "/app.wasm", "/blob.bin" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            request.Headers.AcceptEncoding.ParseAdd("gzip");
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("gzip", response.Content.Headers.ContentEncoding.Single());
            await using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);
            Assert.Equal(payload.Length, (await reader.ReadToEndAsync()).Length);
        }
    }

    private sealed class AbortHost : IAsyncDisposable
    {
        public HttpClient Client { get; }
        public Guid ClientId { get; }
        public GateStream Stream { get; }
        public CancellationTokenSource Cleanup { get; }
        public List<string> Errors { get; }
        private readonly IHost _host;

        private AbortHost(IHost host, HttpClient client, Guid clientId, GateStream stream, CancellationTokenSource cleanup, List<string> errors)
        {
            _host = host;
            Client = client;
            ClientId = clientId;
            Stream = stream;
            Cleanup = cleanup;
            Errors = errors;
        }

        public static async Task<AbortHost> StartAsync()
        {
            var clientId = Guid.NewGuid();
            var cleanup = new CancellationTokenSource();
            var stream = new GateStream(cleanup.Token);
            var errors = new List<string>();
            var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
            state.FileManagerReady.SetResult(true);
            var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            ready.SetResult(state);
            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>
            {
                [clientId.ToString()] = ready
            };
            var host = await new HostBuilder()
                .ConfigureWebHost(web =>
                {
                    web.UseTestServer();
                    web.ConfigureServices(services =>
                    {
                        services.AddSingleton<ILoggerProvider>(new ListLoggerProvider(errors));
                        services.AddMemoryCache();
                        services.AddSingleton(new RemoteFilesOptions { UseClientCache = false, UseServerCache = false });
                        services.AddSingleton(new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance));
                        services.AddSingleton<RemoteFileResolver>(new GateResolver(stream));
                        services.AddSingleton(states);
                    });
                    web.Configure(app => app.UseMiddleware<RemoteFilesMiddleware>());
                })
                .StartAsync();
            return new AbortHost(host, host.GetTestClient(), clientId, stream, cleanup, errors);
        }

        public async ValueTask DisposeAsync()
        {
            Cleanup.Cancel();
            await _host.StopAsync();
            _host.Dispose();
            Client.Dispose();
            Cleanup.Dispose();
        }
    }

    private sealed class GateResolver : RemoteFileResolver
    {
        private readonly GateStream _stream;
        public GateResolver(GateStream stream)
            : base(NullLogger<RemoteFileResolver>.Instance, new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance))
        {
            _stream = stream;
        }

        public override Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subpath)
            => Task.FromResult(new FileMetadata { Length = 5, StatusCode = 200, ETag = "e", LastModified = 0 });

        public override Task<RemoteFileStream> GetFileStreamAsync(string clientId, string subpath)
            => Task.FromResult(new RemoteFileStream { Stream = _stream });
    }

    private sealed class GateStream : Stream
    {
        private readonly CancellationToken _cleanup;
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool UnblockedByCleanup { get; private set; }

        public GateStream(CancellationToken cleanup) => _cleanup = cleanup;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadEntered.TrySetResult();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cleanup);
            try
            {
                await Task.Delay(Timeout.Infinite, linked.Token);
            }
            catch (OperationCanceledException) when (_cleanup.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                UnblockedByCleanup = true;
                return 0;
            }
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private sealed class TokenWriter<T> : IServerStreamWriter<T>
    {
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SawCancelableToken { get; private set; }
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            WriteEntered.TrySetResult();
            return Task.CompletedTask;
        }

        public Task WriteAsync(T message, CancellationToken cancellationToken)
        {
            SawCancelableToken = cancellationToken.CanBeCanceled;
            WriteEntered.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingPingWriter : IServerStreamWriter<PingMessageResponse>
    {
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteStopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(PingMessageResponse message)
        {
            WriteEntered.TrySetResult();
            return Task.Delay(Timeout.Infinite);
        }

        public async Task WriteAsync(PingMessageResponse message, CancellationToken cancellationToken)
        {
            WriteEntered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WriteStopped.TrySetResult();
                throw;
            }
        }
    }

    private sealed class BlockingFileWriter : IServerStreamWriter<ServerFileReadRequest>
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource WriteStopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool UsedToken { get; private set; }
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(ServerFileReadRequest message)
        {
            WriteEntered.TrySetResult();
            return _release.Task;
        }

        public async Task WriteAsync(ServerFileReadRequest message, CancellationToken cancellationToken)
        {
            UsedToken = cancellationToken.CanBeCanceled;
            WriteEntered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WriteStopped.TrySetResult();
                throw;
            }
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class OnePingReader : IAsyncStreamReader<PingMessageRequest>
    {
        private readonly string _id;
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _moves;
        public PingMessageRequest Current { get; private set; } = new();

        public OnePingReader(string id) => _id = id;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_moves++ == 0)
            {
                Current = new PingMessageRequest { Id = _id, Initialize = true, PingIntervalSeconds = 30 };
                return Task.FromResult(true);
            }
            return WaitForEnd(cancellationToken);
        }

        private async Task<bool> WaitForEnd(CancellationToken cancellationToken)
        {
            using var reg = cancellationToken.Register(() => _release.TrySetResult());
            await _release.Task;
            return false;
        }

        public void Release() => _release.TrySetResult();
    }

    private sealed class CallContext : ServerCallContext
    {
        private readonly CancellationToken _token;
        public CallContext(CancellationToken token) => _token = token;
        protected override string MethodCore => "Ping";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => _token;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        private readonly List<string> _errors;
        public ListLoggerProvider(List<string> errors) => _errors = errors;
        public ILogger CreateLogger(string categoryName) => new ListLogger(_errors);
        public void Dispose() { }

        private sealed class ListLogger : ILogger
        {
            private readonly List<string> _errors;
            public ListLogger(List<string> errors) => _errors = errors;
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error)
                    _errors.Add(formatter(state, exception));
            }
        }
    }
}
