using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{

    public class ServiceState : IDisposable, IAsyncDisposable
    {
        public TaskCompletionSource<bool> FileManagerReady { get; set; } = new TaskCompletionSource<bool>();
        public IRequestCookieCollection? Cookies { get; set; }
        private ReasonedCancellationTokenSource? CancellationTokenSource { get; set; }
        /// <summary>The session's source, kept after disposal so its reason can still be read.</summary>
        private readonly ReasonedCancellationTokenSource _lifetime;
        public ILogger<RemoteWebViewService> Logger;
        public CancellationToken Token { get; }
        public string HtmlHostPath { get; init; } = string.Empty;
        public string Markup { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public bool InUse { get; set; } = false;
        public bool Refresh { get; set; } = false;
        // Control transfer: the connection (browser clientId) that currently controls the session (its input reaches the WebView).
        // Empty until the first primary connection claims it. Reassigned by BrowserIPC.SetPrimary.
        public string PrimaryClientId { get; set; } = string.Empty;
        /// <summary>Each browser connection's own secret: what proves a request naming a clientId comes from that connection.</summary>
        public ClientKeys ClientKeys { get; } = new();
        // The connection launched by the machine owner (the local control surface). It may always reclaim control; the remote user never may.
        public string OwnerClientId { get; set; } = string.Empty;
        /// <summary>Who may be given control. Limited to the requester for sessions started through AgentAdmin; see SessionControllers.</summary>
        public SessionControllers Controllers { get; }
        /// <summary>The app's secret for its own owner page (CreateWebView ownerKey). Empty for older apps. See OwnerAccess.</summary>
        public string OwnerKey { get; init; } = string.Empty;
        /// <summary>Given to the owner's page (as a cookie) once it presents the owner key; its connection sends it back.</summary>
        public string OwnerToken { get; } = OwnerAccess.NewToken();
        /// <summary>The app's own secret for its calls (CreateWebView appKey). Empty for older apps. See AppAccess.</summary>
        public string AppKey { get; init; } = string.Empty;
        // Set once the owner has been given control at the start of the session. In mirror mode the owner's surface
        // connects only after the remote user (so the remote user may have claimed first); the first owner connection is
        // still promoted so the user starts in control, but only once - later owner reconnects do not grab control back.
        public bool OwnerControlInitialized { get; set; }

        /// <summary>The owner's own surface opened the controlling /app page (?role=owner); remote users join at /mirror.</summary>
        public bool OwnerHoldsApp { get; set; }
        /// <summary>
        /// The owner's own surface has been given the app's page once. Its first load of /{id} is never a refresh, even when
        /// another browser loaded the page before it (Endpoints.ServesPage); a later load is.
        /// </summary>
        public bool OwnerPageServed { get; set; }
        /// <summary>Set (to 1) by the first accepted restart request, so pressing Restart twice restarts once.</summary>
        public int RestartRequested;
        public string Id { get; init; } = string.Empty;
        public ConcurrentBag<string> IsMirroredConnection { get; set; } = [];
        public bool EnableMirrors { get; set; } = false;       
        public string Group { get; init;  } = string.Empty;
        public int Pid { get; init; } = 0;
        public string ProcessName {  get; init; } = string.Empty;
        public string HostName { get; init; } = string.Empty;
        public string User {  get; set; } = string.Empty;
        public Task? FileReaderTask { get; set; } = null;
        public Task? PingTask { get; set; } = null;
        public TimeSpan MaxClientPing { get; set; } = TimeSpan.Zero;

        public long TotalBytesRead = 0;

        public int TotalFilesRead = 0;
        public TimeSpan TotalFileReadTime { get; set; } = TimeSpan.Zero;
        public TimeSpan MaxFileReadTime { get; set; } = TimeSpan.Zero;
        public Channel<FileEntry> FileCollection { get; set; } = Channel.CreateUnbounded<FileEntry>();
        internal VoiceRoom? Voice { get; init; }
        public IPC IPC { get; }
        public BrowserIPCState BrowserIPC { get; init; } = new();

        public DateTime StartTime { get; } = DateTime.UtcNow;

        /// <summary>
        /// Why the session ended (null while it runs). Set by <see cref="EndAsync"/> in the same call that cancels <see cref="Token"/>,
        /// so code that sees the token fire and finds a reason here knows the cancellation is a follow-on (log at Debug); no reason
        /// means the cancellation came from somewhere else and is worth reporting.
        /// </summary>
        public SessionEndReason? EndReason => _lifetime.Reason;

        /// <summary>Ends the session: cancels its token with <paramref name="reason"/> recorded, then disposes it.</summary>
        public async ValueTask EndAsync(SessionEndReason reason)
        {
            await DisposeAsyncCore(reason).ConfigureAwait(false);
            Dispose(disposing: false);
        }

        private int _disposed = 0; // 0 = not disposed, 1 = disposed

        public void Cancel()
        {
            try
            {
                CancellationTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(disposing: false);
            GC.SuppressFinalize(this);
        }

        protected virtual ValueTask DisposeAsyncCore() => DisposeAsyncCore(null);

        private async ValueTask AwaitDisposalTasks()
        {
            var tasks = new List<Task?> { FileReaderTask, PingTask, IPC?.ProcessMessagesTask, IPC?.ClientTask, IPC?.BrowserTask };
            var timeout = TimeSpan.FromSeconds(30);
            foreach (var task in tasks)
            {
                if (task == null)
                    continue;
                try
                {
                    // None, not the shutdown token: that token is already cancelled and would end the 30s wait immediately.
                    if (await Task.WhenAny(task, Task.Delay(timeout, CancellationToken.None)).ConfigureAwait(false) == task)
                        await task.ConfigureAwait(false);
                    else
                        Logger.LogError("Task timed out during async disposal.");
                }
                catch (OperationCanceledException)
                {
                    // Expected during cancellation.
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "An error occurred while awaiting a task during async disposal.");
                }
            }
        }

        private async ValueTask DisposeAsyncCore(SessionEndReason? reason)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            var cts = CancellationTokenSource;
            CancellationTokenSource = null;
            if (cts != null)
            {
                // Signal cancellation, with the reason recorded first when there is one
                try
                {
                    if (reason is { } why)
                        await cts.CancelAsync(why).ConfigureAwait(false);
                    else
                        await cts.CancelAsync().ConfigureAwait(false);
                }
                catch { }

                try
                {
                    BrowserIPC.Semaphore.Dispose();
                }
                catch { }

                try
                {
                    FileCollection.Writer.TryComplete();
                }
                catch { }

                IPC?.Shutdown();

                await AwaitDisposalTasks().ConfigureAwait(false);

                // Dispose of the CTS after task completion
                try
                {
                    cts.Dispose();
                }
                catch { }
            }

            // Disposal flag already set using Interlocked at the start
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
                return;

            if (disposing)
            {
                var cts = CancellationTokenSource;
                CancellationTokenSource = null;
                if (cts != null)
                {
                    try
                    {
                        cts.Cancel();
                    }
                    catch { }

                    try
                    {
                        BrowserIPC.Semaphore.Dispose();
                    }
                    catch { }

                    try
                    {
                        FileCollection.Writer.TryComplete();
                    }
                    catch { }

                    IPC?.Shutdown();

                    var tasks = new List<Task>();
                    if (FileReaderTask is { IsCompleted: false } fileReaderTask) tasks.Add(fileReaderTask);
                    if (PingTask is { IsCompleted: false } pingTask) tasks.Add(pingTask);
                    if (IPC?.ProcessMessagesTask is { IsCompleted: false } processMessagesTask) tasks.Add(processMessagesTask);
                    if (IPC?.ClientTask is { IsCompleted: false } clientTask) tasks.Add(clientTask);
                    if (IPC?.BrowserTask is { IsCompleted: false } browserTask) tasks.Add(browserTask);

                    if (tasks.Count > 0)
                    {
                        try
                        {
                            Task.WaitAll(tasks.ToArray(), TimeSpan.FromSeconds(5));
                        }
                        catch { }
                    }

                    try
                    {
                        cts.Dispose();
                    }
                    catch { }
                }
            }

            // Free unmanaged resources here, if any...
        }

        public ServiceState(ILogger<RemoteWebViewService> logger, bool enableMirrors)
        {
            EnableMirrors = enableMirrors;
            _lifetime = new ReasonedCancellationTokenSource();
            CancellationTokenSource = _lifetime;
            Token = _lifetime.Token;
            Controllers = new SessionControllers();
            IPC = new IPC(Token,logger,EnableMirrors) { Controllers = Controllers };
            Logger = logger;           
        }
    }
   
}
