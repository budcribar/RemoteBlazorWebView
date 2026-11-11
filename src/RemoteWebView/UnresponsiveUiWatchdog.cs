using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Runs something on the UI thread, with a way out when the UI thread never gets to it.
    /// <para>
    /// A restart request (the relay's "refreshed" notice) is read on a background thread but handed to the app on its UI
    /// thread, where the app's Refreshed handler restarts it. A frozen UI thread never runs that handler, so the one
    /// request meant to recover a stuck app would be lost. Queue priority cannot help: it orders waiting work and cannot
    /// interrupt the work that is stuck.
    /// </para>
    /// <para>
    /// So the UI work is posted as usual, and if it has not started within the timeout the fallback runs instead, on a
    /// thread-pool thread. Exactly one of the two runs: a UI thread that wakes up after the fallback has started does nothing.
    /// </para>
    /// </summary>
    public static class UnresponsiveUiWatchdog
    {
        /// <summary>How long the UI thread has to start the work before it is treated as not responding.</summary>
        public static TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <param name="postToUi">Queues an action on the UI thread without waiting for it (e.g. Dispatcher.InvokeAsync).</param>
        /// <param name="onUi">What to do on the UI thread.</param>
        /// <param name="fallback">What to do, off the UI thread, when the UI thread has not started <paramref name="onUi"/> in time. Null: no watchdog.</param>
        /// <param name="onFallbackError">Told when the fallback itself throws.</param>
        /// <returns>A task that completes once it is settled which of the two runs (for tests; callers need not wait).</returns>
        public static Task Run(Action<Action> postToUi, Action onUi, Action? fallback, TimeSpan? timeout = null, Action<Exception>? onFallbackError = null)
        {
            const int Waiting = 0, OnUi = 1, FellBack = 2;
            var state = Waiting;

            postToUi(() =>
            {
                if (Interlocked.CompareExchange(ref state, OnUi, Waiting) == Waiting)
                    onUi();
            });

            if (fallback is null)
                return Task.CompletedTask;

            return Task.Delay(timeout ?? DefaultTimeout).ContinueWith(_ =>
            {
                if (Interlocked.CompareExchange(ref state, FellBack, Waiting) != Waiting)
                    return; // the UI thread got there first
                try { fallback(); }
                catch (Exception ex) { onFallbackError?.Invoke(ex); }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }
}
