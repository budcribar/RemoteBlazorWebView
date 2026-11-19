using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Services;
using Xunit;

namespace RemoteWebView.Lifecycle.Tests;

/// <summary>Every session shutdown is logged once, at Warning, with its reason and who the app was.</summary>
public class ShutdownLoggingTests
{
    private sealed class CapturingLogger : ILogger<RemoteWebViewService>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception), exception));
    }

    private static (ShutdownService Shutdown, CapturingLogger Log, ServiceState State) Make(string id)
    {
        var log = new CapturingLogger();
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var state = new ServiceState(log, false) { Id = id, InUse = true, ProcessName = "RemotePCDiagnostics", Pid = 5864, HostName = "BUDSLAPTOP", Group = "test-shop" };
        var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.SetResult(state);
        services[id] = ready;
        return (new ShutdownService(log, new ConcurrentDictionary<string, Channel<string>>(), services), log, state);
    }

    [Fact]
    public async Task Shutdown_LogsReasonDetailAndApp_AtWarning()
    {
        var id = Guid.NewGuid().ToString();
        var (shutdown, log, _) = Make(id);

        await shutdown.Shutdown(id, SessionEndReason.ControllerLeft, detail: "controlling browser abc left; no owner");

        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(id, entry.Message);
        Assert.Contains(nameof(SessionEndReason.ControllerLeft), entry.Message);
        Assert.Contains("controlling browser abc left; no owner", entry.Message);
        Assert.Contains("RemotePCDiagnostics pid 5864 on BUDSLAPTOP", entry.Message);
        Assert.Contains("group test-shop", entry.Message);
    }

    [Fact]
    public async Task Shutdown_WithException_AttachesIt()
    {
        var id = Guid.NewGuid().ToString();
        var (shutdown, log, _) = Make(id);
        var error = new InvalidOperationException("stream broken");

        await shutdown.Shutdown(id, SessionEndReason.PingStreamFailed, error);

        var entry = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Same(error, entry.Exception);
        Assert.Contains(nameof(SessionEndReason.PingStreamFailed), entry.Message);
    }

    [Fact]
    public async Task SecondShutdown_OfAnEndedSession_IsNotLoggedAsWarningAgain()
    {
        var id = Guid.NewGuid().ToString();
        var (shutdown, log, _) = Make(id);

        await shutdown.Shutdown(id, SessionEndReason.PingTimeout);
        await shutdown.Shutdown(id, SessionEndReason.PingStreamClosed);

        Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains(nameof(SessionEndReason.PingStreamClosed)));
    }

    [Fact]
    public async Task Shutdown_RecordsEndReasonOnTheState_BeforeItsTokenFires_AndTheFirstReasonWins()
    {
        var id = Guid.NewGuid().ToString();
        var (shutdown, _, state) = Make(id);
        SessionEndReason? seenWhenCancelled = null;
        state.Token.Register(() => seenWhenCancelled = state.EndReason);

        await shutdown.Shutdown(id, SessionEndReason.ControllerLeft);
        await shutdown.Shutdown(id, SessionEndReason.PingStreamClosed);

        Assert.Equal(SessionEndReason.ControllerLeft, seenWhenCancelled);
        Assert.Equal(SessionEndReason.ControllerLeft, state.EndReason);
        Assert.False(state.TrySetEndReason(SessionEndReason.PingTimeout));
    }

    [Fact]
    public async Task HasEnded_RemembersTheReason_UntilANewSessionReusesTheId()
    {
        var id = Guid.NewGuid().ToString();
        var (shutdown, _, _) = Make(id);
        Assert.False(shutdown.HasEnded(id, out _));

        await shutdown.Shutdown(id, SessionEndReason.PingTimeout);

        Assert.True(shutdown.HasEnded(id, out var reason));
        Assert.Equal(SessionEndReason.PingTimeout, reason);
        shutdown.Forget(id);
        Assert.False(shutdown.HasEnded(id, out _));
    }

    [Fact]
    public void A_RunningSession_HasNoEndReason()
    {
        var (_, _, state) = Make(Guid.NewGuid().ToString());
        Assert.Null(state.EndReason);
        Assert.True(state.TrySetEndReason(SessionEndReason.RequestedByApp));
        Assert.Equal(SessionEndReason.RequestedByApp, state.EndReason);
    }
}
