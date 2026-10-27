using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    // Wraps service-dictionary GetOrAdd. The event code is the call site.
    // b=1 means this call inserted the entry. b=0 means it returned one already
    // present. No stack walk: that allocates and would sit in the 90 ms window.
    public static class RegistryLog
    {
        public static TaskCompletionSource<ServiceState> GetOrAdd(
            ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> dictionary,
            string id,
            int site)
        {
            TaskCompletionSource<ServiceState>? minted = null;
            var current = dictionary.GetOrAdd(id, _ =>
            {
                minted = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
                return minted;
            });
            RaceLog.Mark(site, id, b: ReferenceEquals(current, minted) ? 1 : 0);
            return current;
        }
    }
}
