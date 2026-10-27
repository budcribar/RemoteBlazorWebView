using System;
using System.Buffers.Binary;
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
            "reg-create-webview",
            "reg-file-read",
            "reg-client-send-message",
            "reg-ping",
            "reg-start-or-refresh",
            "reg-remote-file",
            "reg-mirror",
            "reg-start",
            "reg-browser-receive",
            "reg-browser-send",
            "reg-client-status",
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

        public static unsafe void Mark(int ev, Guid id = default, int a = 0, int b = 0)
        {
            Header* h = header;
            if (h == null)
                return;

            long seq = Interlocked.Increment(ref h->Next);
            if ((ulong)(seq - 1) >= (ulong)Capacity)
                return;

            Slot* s = slots + (seq - 1);
            s->Ticks = System.Diagnostics.Stopwatch.GetTimestamp();
            s->Pid = CachedPid;
            s->Tid = Environment.CurrentManagedThreadId;
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
                        writer.Write(" a=");
                        writer.Write(s->A);
                        writer.Write(" b=");
                        writer.Write(s->B);
                        writer.WriteLine();
                        last = i + 1;
                    }

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
