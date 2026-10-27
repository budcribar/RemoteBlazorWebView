using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    // In-memory ring for the refresh race. Mark() does no I/O. Flush() runs on
    // process exit, on the restart-failure path, and after a desktop test.
    public static class RaceLog
    {
        public static class Ev
        {
            public const int RefreshHttpEnter = 1;
            public const int RefreshedWritten = 2;
            public const int WaitEnd = 3;
            public const int RestartPage = 4;
            public const int FailurePage = 5;
            public const int FallbackShutdown = 6;
            public const int FallbackShutdownReturn = 7;
            public const int RefreshCatch = 8;
            public const int ShutdownEnter = 9;
            public const int ShutdownRemoved = 10;
            public const int ShutdownWrite = 11;
            public const int ShutdownWriteReturn = 12;
            public const int ShutdownDisposed = 13;
            public const int ShutdownThrow = 14;
            public const int ShutdownMissing = 15;
            public const int CreateEnter = 16;
            public const int CreateFailed = 17;
            public const int CreateCancelled = 18;
            public const int RefreshedBranch = 19;
            public const int ShutdownPost = 20;
            public const int ShutdownPostReturned = 21;
            public const int FirePostReturned = 22;
            public const int CtsCancelled = 23;
            public const int ShutdownCommand = 24;
            public const int BrowserCatch = 25;
            public const int BrowserFinally = 26;
            public const int RpcEnter = 27;
            public const int RpcReturn = 28;
            public const int RpcThrow = 29;
            public const int FirePost = 30;
            public const int FireUiEnter = 31;
            public const int FireUiReturn = 32;
            public const int RestartBefore = 33;
            public const int RestartAfter = 34;
            public const int WpfEnter = 35;
            public const int WpfHandlerEnter = 36;
            public const int WpfHandlerReturn = 37;
            public const int WpfReturn = 38;
            public const int WinEnter = 39;
            public const int WinHandlerEnter = 40;
            public const int WinHandlerReturn = 41;
            public const int WinReturn = 42;
            public const int PhotinoEnter = 43;
            public const int PhotinoHandlerEnter = 44;
            public const int PhotinoHandlerReturn = 45;
            public const int PhotinoReturn = 46;
            public const int DisposeEnter = 47;
            public const int TestClientStarted = 48;
            public const int TestKill = 49;
            public const int CreateFailedReceived = 50;
            public const int RegCreateWebView = 51;
            public const int RegFileRead = 52;
            public const int RegClientSendMessage = 53;
            public const int RegPing = 54;
            public const int RegStartOrRefresh = 55;
            public const int RegRemoteFile = 56;
            public const int RegMirror = 57;
            public const int RegStart = 58;
            public const int RegBrowserReceive = 59;
            public const int RegBrowserSend = 60;
            public const int RegClientStatus = 61;
            // Insert codes are the hit code plus 11. RegistryLog relies on that.
            public const int RegCreateWebViewInsert = 62;
            public const int RegFileReadInsert = 63;
            public const int RegClientSendMessageInsert = 64;
            public const int RegPingInsert = 65;
            public const int RegStartOrRefreshInsert = 66;
            public const int RegRemoteFileInsert = 67;
            public const int RegMirrorInsert = 68;
            public const int RegStartInsert = 69;
            public const int RegBrowserReceiveInsert = 70;
            public const int RegBrowserSendInsert = 71;
            public const int RegClientStatusInsert = 72;
            public const int RegDeregister = 73;
        }

        private static readonly string[] Names =
        {
            "",
            "refresh-http-enter",
            "refresh-http-refreshed-written",
            "refresh-http-wait-end",
            "refresh-http-restart-page",
            "refresh-http-failure-page",
            "refresh-http-fallback-shutdown",
            "refresh-http-fallback-shutdown-return",
            "refresh-http-catch",
            "shutdown-service-enter",
            "shutdown-service-removed",
            "shutdown-service-write",
            "shutdown-service-write-return",
            "shutdown-service-disposed",
            "shutdown-service-throw",
            "shutdown-service-missing",
            "create-webview-enter",
            "create-webview-createFailed",
            "create-webview-cancelled",
            "refreshed-branch-enter",
            "shutdown-post",
            "shutdown-post-returned",
            "firerefreshed-post-returned",
            "cts-cancelled-after-refresh",
            "shutdown-command-received",
            "browser-messages-catch",
            "browser-messages-finally",
            "shutdown-rpc-enter",
            "shutdown-rpc-return",
            "shutdown-rpc-throw",
            "firerefreshed-post",
            "firerefreshed-ui-enter",
            "firerefreshed-ui-return",
            "restart-before-start",
            "restart-after-start",
            "wpf-firerefreshed-enter",
            "wpf-refreshed-handler-enter",
            "wpf-refreshed-handler-return",
            "wpf-firerefreshed-return",
            "winforms-firerefreshed-enter",
            "winforms-refreshed-handler-enter",
            "winforms-refreshed-handler-return",
            "winforms-firerefreshed-return",
            "photino-firerefreshed-enter",
            "photino-refreshed-handler-enter",
            "photino-refreshed-handler-return",
            "photino-firerefreshed-return",
            "dispose-async-core-enter",
            "test-client-started",
            "test-kill",
            "createFailed-received",
            "reg-create-webview-hit",
            "reg-file-read-hit",
            "reg-client-send-message-hit",
            "reg-ping-hit",
            "reg-start-or-refresh-hit",
            "reg-remote-file-hit",
            "reg-mirror-hit",
            "reg-start-hit",
            "reg-browser-receive-hit",
            "reg-browser-send-hit",
            "reg-client-status-hit",
            "reg-create-webview-insert",
            "reg-file-read-insert",
            "reg-client-send-message-insert",
            "reg-ping-insert",
            "reg-start-or-refresh-insert",
            "reg-remote-file-insert",
            "reg-mirror-insert",
            "reg-start-insert",
            "reg-browser-receive-insert",
            "reg-browser-send-insert",
            "reg-client-status-insert",
            "reg-deregister",
        };

        // One desktop pass logs every registry GetOrAdd. 256K slots keep the
        // refresh window if file and browser calls are chatty. Mark() still
        // does no I/O; slots past Capacity are dropped and Flush records that.
        private const int Capacity = 262144;
        private const string MapName = "Local\\RbwRaceRing";

        private struct Header
        {
            public long Next;
            public long Flushed;
            public long Frequency;
            public long StartTicks;
            public long StartUnixMs;
        }

        private struct Slot
        {
            public long Seq;
            public long Ticks;
            public long Id0;
            public long Id1;
            public int Pid;
            public int Tid;
            public int Event;
            public int A;
            public int B;
        }

        private static readonly int CachedPid = Environment.ProcessId;
        private static readonly object InitLock = new object();
        private static MemoryMappedFile? map;
        private static MemoryMappedViewAccessor? view;
        private static unsafe Header* header;
        private static unsafe Slot* slots;
        private static int ready;

        static RaceLog()
        {
            try
            {
                Init();
                AppDomain.CurrentDomain.ProcessExit += static (_, _) => Flush();
            }
            catch
            {
            }
        }

        private static unsafe void Init()
        {
            lock (InitLock)
            {
                if (ready != 0)
                    return;

                long bytes = sizeof(Header) + (long)Capacity * sizeof(Slot);
                map = MemoryMappedFile.CreateOrOpen(MapName, bytes, MemoryMappedFileAccess.ReadWrite);
                view = map.CreateViewAccessor(0, bytes, MemoryMappedFileAccess.ReadWrite);
                byte* ptr = null;
                view.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                header = (Header*)ptr;
                slots = (Slot*)(ptr + sizeof(Header));
                if (Interlocked.CompareExchange(ref header->Frequency, StopwatchFrequency(), 0) == 0)
                {
                    header->StartTicks = Stopwatch.GetTimestamp();
                    header->StartUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
                ready = 1;
            }
        }

        private static long StopwatchFrequency()
        {
            long freq = System.Diagnostics.Stopwatch.Frequency;
            return freq == 0 ? 1 : freq;
        }

        public static void Mark(int ev, Guid id = default, int a = 0, int b = 0)
        {
            MarkAt(ev, id, System.Diagnostics.Stopwatch.GetTimestamp(), CachedPid, Environment.CurrentManagedThreadId, a, b);
        }

        // Writes a pre-stamped tick, pid, and thread id. Used for registry
        // inserts so the log line matches the entry's creation stamp.
        public static unsafe void MarkAt(int ev, Guid id, long ticks, int pid, int tid, int a = 0, int b = 0)
        {
            Header* h = header;
            if (h == null)
                return;

            long seq = Interlocked.Increment(ref h->Next);
            if ((ulong)(seq - 1) >= (ulong)Capacity)
                return;

            Slot* s = slots + (seq - 1);
            s->Ticks = ticks;
            s->Pid = pid;
            s->Tid = tid;
            s->Event = ev;
            s->A = a;
            s->B = b;
            Guid copy = id;
            long* g = (long*)&copy;
            s->Id0 = g[0];
            s->Id1 = g[1];
            Volatile.Write(ref s->Seq, seq);
        }

        public static void Mark(int ev, string? id, int a = 0, int b = 0)
        {
            Guid parsed = default;
            if (id != null)
                Guid.TryParse(id, out parsed);
            Mark(ev, parsed, a, b);
        }

        public static void MarkAt(int ev, string? id, long ticks, int pid, int tid, int a = 0, int b = 0)
        {
            Guid parsed = default;
            if (id != null)
                Guid.TryParse(id, out parsed);
            MarkAt(ev, parsed, ticks, pid, tid, a, b);
        }

        public static unsafe void Flush()
        {
            Header* h = header;
            if (h == null)
                return;

            try
            {
                using var mutex = new Mutex(false, "Local\\RbwRaceFlush");
                bool locked = false;
                try
                {
                    try
                    {
                        locked = mutex.WaitOne(TimeSpan.FromSeconds(5));
                    }
                    catch (AbandonedMutexException)
                    {
                        locked = true;
                    }

                    if (!locked)
                        return;

                    long next = Volatile.Read(ref h->Next);
                    if (next > Capacity)
                        next = Capacity;
                    long flushed = Volatile.Read(ref h->Flushed);
                    if (flushed >= next)
                        return;

                    string dir = Path.Combine(Path.GetTempPath(), "rbw-race");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "race.log");
                    bool writeHeader = !File.Exists(path);
                    using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                    if (writeHeader)
                    {
                        writer.Write("freq=");
                        writer.Write(h->Frequency);
                        writer.Write(" startTicks=");
                        writer.Write(h->StartTicks);
                        writer.Write(" startUnixMs=");
                        writer.Write(h->StartUnixMs);
                        writer.WriteLine();
                    }

                    long last = flushed;
                    for (long i = flushed; i < next; i++)
                    {
                        Slot* s = slots + i;
                        long seq = Volatile.Read(ref s->Seq);
                        if (seq != i + 1)
                        {
                            // A thread can die between taking a sequence number and
                            // publishing it. Skip a hole only when a later slot is
                            // already published, so one torn slot cannot hide the rest.
                            if (!LaterPublished(i, next))
                                break;
                            continue;
                        }

                        int ev = s->Event;
                        string name = (uint)ev < (uint)Names.Length ? Names[ev] : "ev";
                        writer.Write(s->Ticks);
                        writer.Write(" pid=");
                        writer.Write(s->Pid);
                        writer.Write(" tid=");
                        writer.Write(s->Tid);
                        writer.Write(' ');
                        writer.Write(name);
                        writer.Write(" id=");
                        writer.Write(FormatGuid(s->Id0, s->Id1));
                        if (IsRegistryStamp(ev))
                        {
                            writer.Write(" regseq=");
                            writer.Write(s->A);
                        }
                        else
                        {
                            writer.Write(" a=");
                            writer.Write(s->A);
                        }
                        writer.Write(" b=");
                        writer.Write(s->B);
                        writer.WriteLine();
                        last = i + 1;
                    }

                    WriteRegistrationOrder(writer, next);

                    long published = Volatile.Read(ref h->Next);
                    if (published > Capacity)
                    {
                        writer.Write("dropped=");
                        writer.Write(published - Capacity);
                        writer.Write(" next=");
                        writer.Write(published);
                        writer.WriteLine();
                    }

                    writer.Flush();
                    Volatile.Write(ref h->Flushed, last);
                }
                finally
                {
                    if (locked)
                        mutex.ReleaseMutex();
                }
            }
            catch
            {
            }
        }

        private static bool IsRegistryStamp(int ev)
        {
            return ev >= Ev.RegCreateWebViewInsert && ev <= Ev.RegDeregister;
        }

        // Diagnostic only. Orders register and deregister events by the
        // execution ticks stored on them. regseq is reported beside that
        // order and is not used to drop, park, or replace anything.
        private static unsafe void WriteRegistrationOrder(StreamWriter writer, long next)
        {
            var byClient = new Dictionary<ClientKey, List<RegEvent>>();
            for (long i = 0; i < next; i++)
            {
                Slot* s = slots + i;
                if (Volatile.Read(ref s->Seq) != i + 1)
                    continue;
                int ev = s->Event;
                bool insert = ev >= Ev.RegCreateWebViewInsert && ev <= Ev.RegClientStatusInsert;
                if (!insert && ev != Ev.RegDeregister)
                    continue;
                var key = new ClientKey(s->Id0, s->Id1);
                if (!byClient.TryGetValue(key, out var list))
                {
                    list = new List<RegEvent>();
                    byClient.Add(key, list);
                }
                list.Add(new RegEvent(s->Ticks, i, s->Pid, s->Tid, ev, s->A, insert));
            }

            writer.Write("order-begin through=");
            writer.Write(next);
            writer.WriteLine(" timestamps-decide-newest regseq=execution-only");

            foreach (var pair in byClient)
            {
                bool anyDeregister = false;
                for (int n = 0; n < pair.Value.Count; n++)
                {
                    if (!pair.Value[n].Insert)
                    {
                        anyDeregister = true;
                        break;
                    }
                }
                if (!anyDeregister)
                    continue;

                List<RegEvent> events = pair.Value;
                events.Sort(static (x, y) =>
                {
                    int cmp = x.Ticks.CompareTo(y.Ticks);
                    return cmp != 0 ? cmp : x.SlotIndex.CompareTo(y.SlotIndex);
                });

                RegEvent newest = events[events.Count - 1];
                bool deregisterBeforeRegister = false;
                bool publishAgrees = true;
                bool regseqAgrees = true;
                long prevSlot = long.MinValue;
                int prevSeq = int.MinValue;
                for (int n = 0; n < events.Count; n++)
                {
                    RegEvent ev = events[n];
                    if (ev.SlotIndex < prevSlot)
                        publishAgrees = false;
                    if (ev.RegSeq < prevSeq)
                        regseqAgrees = false;
                    prevSlot = ev.SlotIndex;
                    prevSeq = ev.RegSeq;
                    if (ev.Insert)
                        continue;
                    for (int m = 0; m < events.Count; m++)
                    {
                        if (events[m].Insert && ev.Ticks < events[m].Ticks)
                        {
                            deregisterBeforeRegister = true;
                            break;
                        }
                    }
                }

                string id = FormatGuid(pair.Key.Id0, pair.Key.Id1);
                string newestName = (uint)newest.Event < (uint)Names.Length ? Names[newest.Event] : "ev";
                writer.Write("order id=");
                writer.Write(id);
                writer.Write(" events=");
                writer.Write(events.Count);
                writer.Write(" newest=");
                writer.Write(newestName);
                writer.Write(" newestTicks=");
                writer.Write(newest.Ticks);
                writer.Write(" newestRegseq=");
                writer.Write(newest.RegSeq);
                writer.Write(" deregister-before-register=");
                writer.Write(deregisterBeforeRegister ? "yes" : "no");
                writer.Write(" tick-vs-publish=");
                writer.Write(publishAgrees ? "agree" : "out-of-order");
                writer.Write(" tick-vs-regseq=");
                writer.Write(regseqAgrees ? "agree" : "disagree");
                writer.WriteLine();

                for (int n = 0; n < events.Count; n++)
                {
                    RegEvent ev = events[n];
                    bool isNewest = ev.Ticks == newest.Ticks;
                    string name = (uint)ev.Event < (uint)Names.Length ? Names[ev.Event] : "ev";
                    writer.Write("order-event id=");
                    writer.Write(id);
                    writer.Write(" rank=");
                    writer.Write(n + 1);
                    writer.Write(" ticks=");
                    writer.Write(ev.Ticks);
                    writer.Write(" regseq=");
                    writer.Write(ev.RegSeq);
                    writer.Write(" pid=");
                    writer.Write(ev.Pid);
                    writer.Write(" tid=");
                    writer.Write(ev.Tid);
                    writer.Write(' ');
                    writer.Write(name);
                    writer.Write(" newest=");
                    writer.Write(isNewest ? "yes" : "no");
                    if (!ev.Insert && deregisterBeforeRegister)
                    {
                        bool stale = false;
                        for (int m = 0; m < events.Count; m++)
                        {
                            if (events[m].Insert && ev.Ticks < events[m].Ticks)
                            {
                                stale = true;
                                break;
                            }
                        }
                        if (stale)
                            writer.Write(" stale=yes");
                    }
                    if (ev.Insert)
                    {
                        bool after = false;
                        for (int m = 0; m < events.Count; m++)
                        {
                            if (!events[m].Insert && events[m].Ticks < ev.Ticks)
                            {
                                after = true;
                                break;
                            }
                        }
                        if (after)
                            writer.Write(" after-deregister=yes");
                    }
                    writer.WriteLine();
                }
            }
        }

        private readonly struct ClientKey : IEquatable<ClientKey>
        {
            public readonly long Id0;
            public readonly long Id1;

            public ClientKey(long id0, long id1)
            {
                Id0 = id0;
                Id1 = id1;
            }

            public bool Equals(ClientKey other) => Id0 == other.Id0 && Id1 == other.Id1;

            public override bool Equals(object? obj) => obj is ClientKey other && Equals(other);

            public override int GetHashCode() => HashCode.Combine(Id0, Id1);
        }

        private readonly struct RegEvent
        {
            public readonly long Ticks;
            public readonly long SlotIndex;
            public readonly int Pid;
            public readonly int Tid;
            public readonly int Event;
            public readonly int RegSeq;
            public readonly bool Insert;

            public RegEvent(long ticks, long slotIndex, int pid, int tid, int ev, int regSeq, bool insert)
            {
                Ticks = ticks;
                SlotIndex = slotIndex;
                Pid = pid;
                Tid = tid;
                Event = ev;
                RegSeq = regSeq;
                Insert = insert;
            }
        }

        private static unsafe bool LaterPublished(long hole, long next)
        {
            long limit = hole + 64;
            if (limit > next)
                limit = next;
            for (long look = hole + 1; look < limit; look++)
            {
                if (Volatile.Read(ref (slots + look)->Seq) == look + 1)
                    return true;
            }

            return false;
        }

        private static string FormatGuid(long id0, long id1)
        {
            if (id0 == 0 && id1 == 0)
                return "-";

            Span<byte> bytes = stackalloc byte[16];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, id0);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.Slice(8), id1);
            return new Guid(bytes).ToString();
        }
    }
}
