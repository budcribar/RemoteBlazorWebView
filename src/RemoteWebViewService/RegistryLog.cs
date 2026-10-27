using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    // Dictionary value created only when GetOrAdd inserts. The creation stamp
    // is the same Stopwatch clock the ring uses. Nothing in the refresh wait
    // reads these fields.
    public sealed class ClientRegistration : TaskCompletionSource<ServiceState>
    {
        public readonly long CreatedTicks;
        public readonly int ServerPid;
        public readonly int ServerTid;

        public ClientRegistration() : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
            CreatedTicks = Stopwatch.GetTimestamp();
            ServerPid = Environment.ProcessId;
            ServerTid = Environment.CurrentManagedThreadId;
        }
    }

    // Wraps service-dictionary GetOrAdd. An insert logs reg-*-insert with the
    // entry's creation ticks, server pid, and thread id. A hit logs reg-*-hit.
    // No stack walk and no extra allocation on the hit path.
    public static class RegistryLog
    {
        private const int InsertDelta = RaceLog.Ev.RegCreateWebViewInsert - RaceLog.Ev.RegCreateWebView;

        public static TaskCompletionSource<ServiceState> GetOrAdd(
            ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> dictionary,
            string id,
            int site)
        {
            ClientRegistration? minted = null;
            var current = dictionary.GetOrAdd(id, _ =>
            {
                minted = new ClientRegistration();
                return minted;
            });
            if (ReferenceEquals(current, minted))
            {
                RaceLog.MarkAt(site + InsertDelta, id, minted!.CreatedTicks, minted.ServerPid, minted.ServerTid, b: 1);
            }
            else
            {
                RaceLog.Mark(site, id, b: 0);
            }
            return current;
        }
    }
}
