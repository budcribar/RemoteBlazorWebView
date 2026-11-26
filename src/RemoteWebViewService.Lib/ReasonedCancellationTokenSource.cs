using System.Threading;
using System.Threading.Tasks;
using PeakSWC.RemoteWebView.Services;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// A cancellation source that records why it was cancelled. <see cref="CancellationToken"/> is a struct and carries no reason, so
    /// the reason lives here and is set inside <see cref="Cancel(SessionEndReason)"/> before the token fires: anything that reacts to
    /// the token and then reads <see cref="Reason"/> sees it. The first reason wins. Code that holds only the token (or a linked
    /// token) can't reach the source, so it reads the reason through whatever owns the source (ServiceState.EndReason).
    /// </summary>
    public sealed class ReasonedCancellationTokenSource : CancellationTokenSource
    {
        private int _reason = -1;

        /// <summary>Why the source was cancelled; null while it hasn't been, or when it was cancelled without a reason.</summary>
        public SessionEndReason? Reason
        {
            get
            {
                var reason = Volatile.Read(ref _reason);
                return reason < 0 ? null : (SessionEndReason)reason;
            }
        }

        /// <summary>Records <paramref name="reason"/> (unless one is recorded already), then cancels. Returns true when this call's reason was recorded.</summary>
        public bool Cancel(SessionEndReason reason)
        {
            var recorded = Record(reason);
            Cancel();
            return recorded;
        }

        /// <summary>Same as <see cref="Cancel(SessionEndReason)"/>, awaiting the token's cancellation callbacks.</summary>
        public async ValueTask<bool> CancelAsync(SessionEndReason reason)
        {
            var recorded = Record(reason);
            await base.CancelAsync().ConfigureAwait(false);
            return recorded;
        }

        private bool Record(SessionEndReason reason) =>
            Interlocked.CompareExchange(ref _reason, (int)reason, -1) == -1;
    }
}
