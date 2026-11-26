using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Joins a caller to a session's registration (the entry in the session dictionary) for as long as it waits on it.
    /// An entry that never completed is removed only when its last waiter leaves, so one waiter giving up does not
    /// strand another one, or the app registering at that moment, on an entry that is no longer in the dictionary.
    /// </summary>
    public static class PendingSessions
    {
        private sealed class Waiters
        {
            public int Count;
            public bool Abandoned;
        }

        private static readonly ConditionalWeakTable<TaskCompletionSource<ServiceState>, Waiters> waiters = new();

        private static TaskCompletionSource<ServiceState> NewSource() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Gets or adds the entry for <paramref name="id"/> and joins it. Dispose the result when done waiting.</summary>
        public static PendingSession Join(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> sessions, string id)
        {
            while (true)
            {
                var source = sessions.GetOrAdd(id, _ => NewSource());
                if (TryEnter(source))
                    return new PendingSession(sessions, id, source);
                // The entry was abandoned (and removed) after we read it: take the next one.
            }
        }

        /// <summary>Joins the entry for <paramref name="id"/> only when one exists; never adds one.</summary>
        public static PendingSession? TryJoin(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> sessions, string id)
        {
            while (sessions.TryGetValue(id, out var source))
            {
                if (TryEnter(source))
                    return new PendingSession(sessions, id, source);
            }
            return null;
        }

        private static bool TryEnter(TaskCompletionSource<ServiceState> source)
        {
            var w = waiters.GetValue(source, _ => new Waiters());
            lock (w)
            {
                if (w.Abandoned)
                    return false;
                w.Count++;
                return true;
            }
        }

        internal static void Leave(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> sessions, string id, TaskCompletionSource<ServiceState> source)
        {
            if (!waiters.TryGetValue(source, out var w))
                return;
            lock (w)
            {
                w.Count--;
                if (w.Count > 0 || source.Task.IsCompleted)
                    return;
                // Last waiter and nobody registered: drop the entry. Done under the lock so no new waiter joins it meanwhile.
                w.Abandoned = true;
                sessions.TryRemove(KeyValuePair.Create(id, source));
            }
        }
    }

    /// <summary>One caller's hold on a session entry; see <see cref="PendingSessions"/>.</summary>
    public sealed class PendingSession : IDisposable
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> sessions;
        private readonly string id;
        private int disposed;

        internal PendingSession(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> sessions, string id, TaskCompletionSource<ServiceState> source)
        {
            this.sessions = sessions;
            this.id = id;
            Source = source;
        }

        public TaskCompletionSource<ServiceState> Source { get; }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref disposed, 1) == 0)
                PendingSessions.Leave(sessions, id, Source);
        }
    }
}
