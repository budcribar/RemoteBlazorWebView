using System.Collections.Concurrent;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public class BrowserIPCState
    {
        public ConcurrentDictionary<uint, SendSequenceMessageRequest> MessageDictionary { get; } = new();
        public uint SequenceNum { get; set; } = 1;
        public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);
    }
}
