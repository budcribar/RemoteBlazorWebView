using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

public class PingShutdownTests
{
    [Fact]
    public async Task Ping_finishes_within_a_second_when_the_service_shuts_down()
    {
        var id = Guid.NewGuid().ToString();
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false)
        {
            Id = id
        };
        var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.SetResult(state);
        services[id] = ready;

        var channels = new ConcurrentDictionary<string, Channel<string>>();
        using var files = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(
            NullLogger<RemoteWebViewService>.Instance,
            services,
            channels,
            new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services),
            files);

        var reader = new ShutdownPingReader(id);
        using var callCancelled = new CancellationTokenSource();
        var ping = service.Ping(reader, new QuietPingWriter(), new CallContext(callCancelled.Token));
        try
        {
            await reader.SecondReadEntered.WaitAsync(TimeSpan.FromSeconds(2));
            state.Cancel();
            await ping.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.True(ping.IsCompletedSuccessfully);
        }
        finally
        {
            state.Cancel();
            callCancelled.Cancel();
            reader.Release();
            await ping.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    private sealed class ShutdownPingReader : IAsyncStreamReader<PingMessageRequest>
    {
        private readonly string _id;
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _moves;
        public PingMessageRequest Current { get; private set; } = new();
        public Task SecondReadEntered => _entered.Task;

        public ShutdownPingReader(string id) => _id = id;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_moves++ == 0)
            {
                Current = new PingMessageRequest { Id = _id, Initialize = true, PingIntervalSeconds = 30 };
                return Task.FromResult(true);
            }

            _entered.TrySetResult();
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

    private sealed class QuietPingWriter : IServerStreamWriter<PingMessageResponse>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(PingMessageResponse message) => Task.CompletedTask;
        public Task WriteAsync(PingMessageResponse message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
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
}
