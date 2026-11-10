using System.Collections.Concurrent;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public class BrowserIPCState
    {
        public ConcurrentDictionary<uint, SendSequenceMessageRequest> MessageDictionary { get; } = new();
        public uint SequenceNum { get; set; } = 1;
        public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Held while deciding and carrying out a change of controller, so two changes at once (give and take pressed
        /// together, or a hand-off while the controller is leaving) happen one after the other: each decides from the
        /// result of the one before, and browsers are told about them in the order they happened.
        /// </summary>
        public SemaphoreSlim ControlChange { get; } = new SemaphoreSlim(1, 1);
    }
}
