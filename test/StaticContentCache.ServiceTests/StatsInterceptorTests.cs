using Grpc.Core;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

public class StatsInterceptorTests
{
    [Fact]
    public async Task Unary_call_records_one_success_and_the_message_sizes()
    {
        var stats = new ServerStats();
        var interceptor = new StatsInterceptor(stats);
        var request = new StringRequest { Request = "in" };
        var response = new StringRequest { Request = "out" };

        var result = await interceptor.UnaryServerHandler(request, new TestCallContext(), (_, _) => Task.FromResult(response));

        Assert.Same(response, result);
        Assert.Equal(1L, Stat(stats, "TotalRequests"));
        Assert.Equal(1L, Stat(stats, "SuccessfulRequests"));
        Assert.Equal(0L, Stat(stats, "FailedRequests"));
        Assert.Equal(0L, Stat(stats, "ActiveConnections"));
        Assert.Equal(stats.CalculateMessageSize(request), Stat(stats, "TotalBytesReceived"));
        Assert.Equal(stats.CalculateMessageSize(response), Stat(stats, "TotalBytesSent"));
    }

    [Fact]
    public async Task Unary_rpc_exception_is_unchanged_and_counted_as_its_status()
    {
        var stats = new ServerStats();
        var interceptor = new StatsInterceptor(stats);
        var request = new StringRequest { Request = "in" };
        var rpc = new RpcException(new Status(StatusCode.NotFound, "missing"));

        var thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<StringRequest, StringRequest>(request, new TestCallContext(), (_, _) => throw rpc));

        Assert.Same(rpc, thrown);
        Assert.Equal(1L, Stat(stats, "FailedRequests"));
        Assert.Equal(0L, Stat(stats, "SuccessfulRequests"));
        Assert.Equal(0L, Stat(stats, "ActiveConnections"));
        Assert.Equal(stats.CalculateMessageSize(request), Stat(stats, "TotalBytesReceived"));
        Assert.Equal(0L, Stat(stats, "TotalBytesSent"));
        var errors = Assert.IsType<Dictionary<string, long>>(stats.GetStats()["ErrorTypes"]);
        Assert.Equal(1L, errors[StatusCode.NotFound.ToString()]);
    }

    [Fact]
    public async Task Unary_exception_becomes_an_internal_rpc_exception()
    {
        var stats = new ServerStats();
        var interceptor = new StatsInterceptor(stats);
        var request = new StringRequest { Request = "in" };

        var thrown = await Assert.ThrowsAsync<RpcException>(() =>
            interceptor.UnaryServerHandler<StringRequest, StringRequest>(
                request,
                new TestCallContext(),
                (_, _) => throw new InvalidOperationException("boom")));

        Assert.Equal(StatusCode.Internal, thrown.StatusCode);
        Assert.Equal("boom", thrown.Status.Detail);
        Assert.Null(thrown.Status.DebugException);
        Assert.Equal(1L, Stat(stats, "FailedRequests"));
        Assert.Equal(0L, Stat(stats, "ActiveConnections"));
        var errors = Assert.IsType<Dictionary<string, long>>(stats.GetStats()["ErrorTypes"]);
        Assert.Equal(1L, errors[nameof(InvalidOperationException)]);
    }

    [Fact]
    public async Task Server_stream_records_bytes_written_by_the_continuation()
    {
        var stats = new ServerStats();
        var interceptor = new StatsInterceptor(stats);
        var request = new StringRequest { Request = "in" };
        var sent = new StringRequest { Request = "stream" };
        var writer = new CollectingWriter<StringRequest>();

        await interceptor.ServerStreamingServerHandler(
            request,
            writer,
            new TestCallContext(),
            async (_, stream, _) => await stream.WriteAsync(sent));

        Assert.Equal(new[] { sent }, writer.Messages);
        Assert.Equal(1L, Stat(stats, "SuccessfulRequests"));
        Assert.Equal(stats.CalculateMessageSize(request), Stat(stats, "TotalBytesReceived"));
        Assert.Equal(stats.CalculateMessageSize(sent), Stat(stats, "TotalBytesSent"));
        Assert.Equal(0L, Stat(stats, "ActiveConnections"));
    }

    [Fact]
    public async Task Client_stream_records_bytes_read_and_the_response_size()
    {
        var stats = new ServerStats();
        var interceptor = new StatsInterceptor(stats);
        var incoming = new StringRequest { Request = "chunk" };
        var response = new StringRequest { Request = "done" };

        var result = await interceptor.ClientStreamingServerHandler(
            new SingleReader<StringRequest>(incoming),
            new TestCallContext(),
            async (stream, _) =>
            {
                Assert.True(await stream.MoveNext(CancellationToken.None));
                Assert.Same(incoming, stream.Current);
                Assert.False(await stream.MoveNext(CancellationToken.None));
                return response;
            });

        Assert.Same(response, result);
        Assert.Equal(1L, Stat(stats, "SuccessfulRequests"));
        // The reader counts the chunk locally and the handler never copies that count.
        Assert.Equal(0L, Stat(stats, "TotalBytesReceived"));
        Assert.Equal(stats.CalculateMessageSize(response), Stat(stats, "TotalBytesSent"));
        Assert.Equal(0L, Stat(stats, "ActiveConnections"));
    }

    [Fact]
    public async Task Duplex_stream_records_both_directions()
    {
        var stats = new ServerStats();
        var interceptor = new StatsInterceptor(stats);
        var incoming = new StringRequest { Request = "up" };
        var outgoing = new StringRequest { Request = "down" };
        var writer = new CollectingWriter<StringRequest>();

        await interceptor.DuplexStreamingServerHandler(
            new SingleReader<StringRequest>(incoming),
            writer,
            new TestCallContext(),
            async (requests, responses, _) =>
            {
                Assert.True(await requests.MoveNext(CancellationToken.None));
                await responses.WriteAsync(outgoing);
            });

        Assert.Equal(new[] { outgoing }, writer.Messages);
        Assert.Equal(1L, Stat(stats, "SuccessfulRequests"));
        // The request reader count is not copied onto the server stats.
        Assert.Equal(0L, Stat(stats, "TotalBytesReceived"));
        Assert.Equal(stats.CalculateMessageSize(outgoing), Stat(stats, "TotalBytesSent"));
        Assert.Equal(0L, Stat(stats, "ActiveConnections"));
    }

    private static long Stat(ServerStats stats, string name) => (long)stats.GetStats()[name];

    private sealed class TestCallContext : ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => null!;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    private sealed class CollectingWriter<T> : IServerStreamWriter<T>
    {
        public List<T> Messages { get; } = [];
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class SingleReader<T>(T item) : IAsyncStreamReader<T>
    {
        private bool _moved;
        public T Current { get; private set; } = default!;

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (_moved)
                return Task.FromResult(false);
            _moved = true;
            Current = item;
            return Task.FromResult(true);
        }
    }
}
