using System.Collections.Concurrent;
using System.Threading.Channels;
using Grpc.Core;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Agent;
using PeakSWC.RemoteWebView.Agents;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

/// <summary>
/// RemoteBlazorWebView#86, item 1 and 3: an end the admin site or connector asks for is an expected disconnect. The relay ends its
/// side of the session first, with a reason and who asked, and the app's streams aborting a moment later log at Debug, not as failures.
/// </summary>
public class AgentEndSessionTests
{
    private const string ServerUrl = "https://relay.example";

    [Fact]
    public async Task EndSession_ends_the_relay_session_with_the_requester_before_the_agent_is_told()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var started = registry.StartSession("m-1", false, "Pat Lee (AI connector)", ServerUrl, true, true).Response;
        Assert.True(connection.Commands.TryRead(out _));
        registry.OnProgress(connection, new LaunchProgress { RequestId = started.RequestId, Stage = LaunchStage.StageReady, SessionId = started.SessionId });

        var relay = new FakeRelay(started.SessionId);
        bool endedBeforeCancel = false;
        relay.State.Token.Register(() => endedBeforeCancel = !connection.Commands.TryPeek(out _));
        var adminLog = new CapturingLogger<AgentAdminService>();
        var service = new AgentAdminService(adminLog, registry, AgentOptions.Create(null, null), shutdownService: relay.Shutdown);

        var response = await service.EndSession(new EndSessionRequest { MachineId = "m-1", Requester = "Pat Lee (AI connector)" }, new CallContext(CancellationToken.None));

        Assert.Equal(EndSessionOutcome.EndSessionRequested, response.Outcome);
        Assert.Equal(started.RequestId, response.RequestId);
        Assert.True(endedBeforeCancel);
        Assert.True(connection.Commands.TryRead(out var cancel));
        Assert.Equal(started.RequestId, cancel!.Cancel.RequestId);

        Assert.True(relay.Shutdown.HasEnded(started.SessionId, out var reason));
        Assert.Equal(SessionEndReason.EndRequestedByAdmin, reason);
        Assert.Equal(SessionEndReason.EndRequestedByAdmin, relay.State.EndReason);
        var warning = Assert.Single(relay.Log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("by Pat Lee (AI connector)", warning.Message);
        Assert.DoesNotContain(relay.Log.Entries, e => e.Level >= LogLevel.Error);

        Assert.Contains(adminLog.Entries, e => e.Message.Contains("Diagnostics end requested on PC-m-1 (m-1) by Pat Lee (AI connector): request " + started.RequestId));
    }

    [Fact]
    public async Task EndSession_that_cannot_end_anything_leaves_the_relay_session_alone()
    {
        var registry = new AgentRegistry();
        registry.Register(Hello("m-1"), "10.0.0.1", null);
        var relay = new FakeRelay(Guid.NewGuid().ToString());
        var service = new AgentAdminService(NullLogger<AgentAdminService>.Instance, registry, AgentOptions.Create(null, null), shutdownService: relay.Shutdown);

        var response = await service.EndSession(new EndSessionRequest { MachineId = "m-1" }, new CallContext(CancellationToken.None));

        Assert.Equal(EndSessionOutcome.EndSessionNotRunning, response.Outcome);
        Assert.False(relay.State.Token.IsCancellationRequested);
        Assert.Empty(relay.Log.Entries);
    }

    [Fact]
    public void PeekEndSession_reports_the_session_without_sending_the_cancel()
    {
        var registry = new AgentRegistry();
        var connection = registry.Register(Hello("m-1"), "10.0.0.1", null);
        var started = registry.StartSession("m-1", false, "Bob", ServerUrl, true, true).Response;
        Assert.True(connection.Commands.TryRead(out _));

        var peek = registry.PeekEndSession("m-1");

        Assert.Equal(EndSessionOutcome.EndSessionRequested, peek.Outcome);
        Assert.Equal(started.SessionId, peek.SessionId);
        Assert.False(connection.Commands.TryRead(out _));
        Assert.Equal(started.SessionId, registry.EndSession("m-1").SessionId);
        Assert.True(connection.Commands.TryRead(out _));
    }

    [Fact]
    public async Task Ping_abort_after_the_session_ended_logs_at_debug()
    {
        var relay = new FakeRelay(Guid.NewGuid().ToString());
        var reader = new AbortingPingReader(relay.Id, () => relay.Shutdown.Shutdown(relay.Id, SessionEndReason.EndRequestedByAdmin));

        await relay.Service.Ping(reader, new QuietWriter<PingMessageResponse>(), new CallContext(CancellationToken.None));

        Assert.DoesNotContain(relay.Log.Entries, e => e.Level >= LogLevel.Error);
        Assert.Contains(relay.Log.Entries, e => e.Level == LogLevel.Debug && e.Exception is IOException);
    }

    [Fact]
    public async Task Ping_abort_of_a_running_session_is_still_an_error()
    {
        var relay = new FakeRelay(Guid.NewGuid().ToString());
        var reader = new AbortingPingReader(relay.Id, () => Task.CompletedTask);

        await relay.Service.Ping(reader, new QuietWriter<PingMessageResponse>(), new CallContext(CancellationToken.None));

        Assert.Contains(relay.Log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Exception in Ping"));
        Assert.True(relay.Shutdown.HasEnded(relay.Id, out var reason));
        Assert.Equal(SessionEndReason.PingStreamFailed, reason);
    }

    [Fact]
    public async Task File_read_abort_after_the_session_ended_logs_at_debug()
    {
        var relay = new FakeRelay(Guid.NewGuid().ToString());
        var reader = new AbortingFileReader(relay.Id, () => relay.Shutdown.Shutdown(relay.Id, SessionEndReason.EndRequestedByAdmin));

        await relay.Service.RequestClientFileRead(reader, new QuietWriter<ServerFileReadRequest>(), new CallContext(CancellationToken.None));

        Assert.DoesNotContain(relay.Log.Entries, e => e.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task File_read_abort_of_a_running_session_is_still_an_error()
    {
        var relay = new FakeRelay(Guid.NewGuid().ToString());
        var reader = new AbortingFileReader(relay.Id, () => Task.CompletedTask);

        await relay.Service.RequestClientFileRead(reader, new QuietWriter<ServerFileReadRequest>(), new CallContext(CancellationToken.None));

        Assert.Contains(relay.Log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("Error handling client"));
    }

    [Fact]
    public async Task Shutdown_notice_to_an_app_whose_stream_has_completed_is_not_an_error()
    {
        var relay = new FakeRelay(Guid.NewGuid().ToString());
        relay.State.IPC.ClientResponseStream = new CompletedWriter<WebMessageResponse>();

        await relay.Shutdown.Shutdown(relay.Id, SessionEndReason.PingStreamFailed);

        Assert.DoesNotContain(relay.Log.Entries, e => e.Level >= LogLevel.Error);
        Assert.Contains(relay.Log.Entries, e => e.Level == LogLevel.Debug && e.Exception is InvalidOperationException);
    }

    // ---------------------------------------------------------------- helpers

    private sealed class FakeRelay
    {
        public string Id { get; }
        public CapturingLogger<RemoteWebViewService> Log { get; } = new();
        public ServiceState State { get; }
        public ShutdownService Shutdown { get; }
        public RemoteWebViewService Service { get; }

        public FakeRelay(string id)
        {
            Id = id;
            var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
            State = new ServiceState(Log, false) { Id = id, InUse = true, ProcessName = "RemotePCDiagnostics", Pid = 1, HostName = "PC-m-1", Group = "" };
            var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            ready.SetResult(State);
            services[id] = ready;
            var channels = new ConcurrentDictionary<string, Channel<string>>();
            Shutdown = new ShutdownService(Log, channels, services);
            Service = new RemoteWebViewService(Log, services, channels, Shutdown, new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance));
        }
    }

    /// <summary>The app's HTTP/2 connection faulting when the agent closes it.</summary>
    private static IOException Abort() => new("The request stream was aborted.", new ConnectionAbortedException("The HTTP/2 connection faulted."));

    private sealed class AbortingPingReader(string id, Func<Task> beforeAbort) : IAsyncStreamReader<PingMessageRequest>
    {
        private int moves;
        public PingMessageRequest Current { get; private set; } = new();

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (moves++ == 0)
            {
                Current = new PingMessageRequest { Id = id, Initialize = false };
                return true;
            }
            await beforeAbort();
            throw Abort();
        }
    }

    private sealed class AbortingFileReader(string id, Func<Task> beforeAbort) : IAsyncStreamReader<ClientFileReadResponse>
    {
        private int moves;
        public ClientFileReadResponse Current { get; private set; } = new();

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (moves++ == 0)
            {
                Current = new ClientFileReadResponse { ClientId = id, Init = new Init { HtmlHostPath = "wwwroot" } };
                return true;
            }
            await beforeAbort();
            throw Abort();
        }
    }

    private sealed class QuietWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => Task.CompletedTask;
    }

    private sealed class CompletedWriter<T> : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => throw new InvalidOperationException("Can't write the message because the request is complete.");
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message, Exception? Exception)> entries = new();
        public IReadOnlyCollection<(LogLevel Level, string Message, Exception? Exception)> Entries => entries.ToArray();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            entries.Enqueue((logLevel, formatter(state, exception), exception));
    }

    private static AgentHello Hello(string machineId) => new()
    {
        MachineId = machineId,
        MachineName = "PC-" + machineId,
        AgentVersion = "1.0.0",
        Lifetime = Lifetime.Persistent,
    };

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
