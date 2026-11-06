using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView.Agent;
using PeakSWC.RemoteWebView.Agents;

namespace StaticContentCache.Tests;

public class AgentAdminCancellationTests
{
    [Fact]
    public async Task WatchAgents_passes_the_call_token_so_a_cancelled_write_ends_the_watch()
    {
        var registry = new AgentRegistry();
        var service = new AgentAdminService(NullLogger<AgentAdminService>.Instance, registry, AgentOptions.Create(null, null));
        using var callCancelled = new CancellationTokenSource();
        var writer = new BlockingListWriter();

        var watch = service.WatchAgents(new Empty(), writer, new CallContext(callCancelled.Token));
        await writer.Entered.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(writer.Token.CanBeCanceled);

        callCancelled.Cancel();
        var finished = await Task.WhenAny(watch, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(watch, finished);
        await watch;
    }

    private sealed class BlockingListWriter : IServerStreamWriter<AgentList>
    {
        private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => entered.Task;
        public CancellationToken Token { get; private set; }
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(AgentList message)
        {
            entered.TrySetResult();
            return Task.Delay(Timeout.Infinite);
        }

        public async Task WriteAsync(AgentList message, CancellationToken cancellationToken)
        {
            Token = cancellationToken;
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class CallContext(CancellationToken token) : ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => token;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore { get; } = new(null, new Dictionary<string, List<AuthProperty>>());
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => throw new NotSupportedException();
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
