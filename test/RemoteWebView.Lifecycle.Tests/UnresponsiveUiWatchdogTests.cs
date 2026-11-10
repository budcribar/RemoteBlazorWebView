extern alias client;

using ClientRemote = client::PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

/// <summary>
/// A restart request is handed to the app on its UI thread. When that thread is stuck the request must still restart the
/// app, through the fallback the app registered, and never both ways.
/// </summary>
public class UnresponsiveUiWatchdogTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task A_responsive_ui_thread_runs_the_work_and_the_fallback_never_runs()
    {
        var onUi = 0; var fallback = 0;

        await ClientRemote.UnresponsiveUiWatchdog.Run(work => work(), () => onUi++, () => fallback++, Short);

        Assert.Equal(1, onUi);
        Assert.Equal(0, fallback);
    }

    [Fact]
    public async Task A_stuck_ui_thread_gets_the_fallback_instead()
    {
        var onUi = 0; var fallback = 0;
        Action? queued = null; // the UI thread never gets to it

        await ClientRemote.UnresponsiveUiWatchdog.Run(work => queued = work, () => onUi++, () => fallback++, Short);

        Assert.Equal(0, onUi);
        Assert.Equal(1, fallback);
        Assert.NotNull(queued);
    }

    [Fact]
    public async Task A_ui_thread_that_wakes_up_after_the_fallback_does_not_restart_a_second_time()
    {
        var onUi = 0; var fallback = 0;
        Action? queued = null;

        await ClientRemote.UnresponsiveUiWatchdog.Run(work => queued = work, () => onUi++, () => fallback++, Short);
        queued!(); // the UI thread finally runs what was queued

        Assert.Equal(0, onUi);
        Assert.Equal(1, fallback);
    }

    [Fact]
    public async Task A_slow_ui_thread_that_starts_in_time_keeps_the_fallback_from_running()
    {
        var onUi = 0; var fallback = 0;
        Action? queued = null;

        var settled = ClientRemote.UnresponsiveUiWatchdog.Run(work => queued = work, () => onUi++, () => fallback++, TimeSpan.FromSeconds(2));
        await Task.Delay(50);
        queued!(); // busy, but within the timeout
        await settled;

        Assert.Equal(1, onUi);
        Assert.Equal(0, fallback);
    }

    [Fact]
    public async Task Without_a_fallback_nothing_changes()
    {
        var onUi = 0;
        Action? queued = null;

        await ClientRemote.UnresponsiveUiWatchdog.Run(work => queued = work, () => onUi++, null, Short);

        Assert.Equal(0, onUi); // still waiting for the UI thread, exactly as before
        queued!();
        Assert.Equal(1, onUi);
    }

    [Fact]
    public async Task A_fallback_that_throws_is_reported_and_does_not_escape()
    {
        Exception? reported = null;

        await ClientRemote.UnresponsiveUiWatchdog.Run(_ => { }, () => { }, () => throw new InvalidOperationException("no"), Short, ex => reported = ex);

        Assert.IsType<InvalidOperationException>(reported);
    }
}
