using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
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

        // Execution-order counter for register and deregister events only.
        // Assigned when the dictionary call runs, not when a request is queued.
        // Nothing in the registry or the refresh wait reads it.
        private static int executionSeq;

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
                int regseq = Interlocked.Increment(ref executionSeq);
                RaceLog.MarkAt(site + InsertDelta, id, minted!.CreatedTicks, minted.ServerPid, minted.ServerTid, a: regseq, b: 1);
            }
            else
            {
                RaceLog.Mark(site, id, b: 0);
            }
            return current;
        }

        // Same TryRemove the shutdown path already used. Ticks are sampled
        // here, at the remove, and regseq is assigned only after it succeeds.
        public static bool TryRemove(
            ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> dictionary,
            string id,
            out TaskCompletionSource<ServiceState> client)
        {
            long ticks = Stopwatch.GetTimestamp();
            int pid = Environment.ProcessId;
            int tid = Environment.CurrentManagedThreadId;
            if (!dictionary.TryRemove(id, out TaskCompletionSource<ServiceState>? removed) || removed == null)
            {
                client = null!;
                return false;
            }

            client = removed;
            int regseq = Interlocked.Increment(ref executionSeq);
            RaceLog.MarkAt(RaceLog.Ev.RegDeregister, id, ticks, pid, tid, a: regseq, b: 1);
            return true;
        }
    }
}
