using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{

    public class ServiceState : IDisposable
    {
        public IRequestCookieCollection? Cookies { get; set; }
        private CancellationTokenSource? CancellationTokenSource { get; set; }
        public ILogger<RemoteWebViewService> Logger;
        public CancellationToken Token { get; }
        public string HtmlHostPath { get; init; } = string.Empty;
        public string Markup { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public bool InUse { get; set; } = false;
        public bool Refresh { get; set; } = false;
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
        public long TotalBytesRead { get; set; } = 0;
        public int TotalFilesRead { get; set; } = 0;
        public TimeSpan TotalFileReadTime { get; set; } = TimeSpan.Zero;
        public TimeSpan MaxFileReadTime { get; set; } = TimeSpan.Zero;
        public ConcurrentDictionary<string, ConcurrentList<FileEntry>> FileDictionary { get; set; } = new();
        public Channel<FileEntry> FileCollection { get; set; } = Channel.CreateUnbounded<FileEntry>();
        public IPC IPC { get; }
        public BrowserIPCState BrowserIPC { get; init; } = new();

        public DateTime StartTime { get; } = DateTime.Now;

        private bool _disposed = false;
        private readonly object _disposeLock = new object();

        public void Cancel()
        {
            CancellationTokenSource?.Cancel();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed)
                return;

            if (CancellationTokenSource != null)
            {
                // Signal cancellation
                CancellationTokenSource.Cancel();


                // Await all relevant tasks
                var tasks = new List<Task?> { FileReaderTask, PingTask, IPC?.ProcessMessagesTask, IPC?.ClientTask, IPC?.BrowserTask };

                foreach (var task in tasks)
                {
                    if (task != null)
                    {
                        try
                        {
                            await task.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            // Expected during cancellation; no action needed
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "An error occurred while awaiting a task during async disposal.");
                        }
                    }
                }

                // Dispose of the CTS after task completion
                CancellationTokenSource.Dispose();
                CancellationTokenSource = null;
            }

            _disposed = true;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            lock (_disposeLock)
            {
                if (_disposed)
                    return;

                if (disposing)
                {
                    foreach (var entry in FileDictionary)
                    {
                        try
                        {
                            entry.Value?.Dispose();
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Error disposing FileDictionary entry with key: {Key}.", entry.Key);
                        }
                    }

                    FileDictionary.Clear();
                }

                // Free unmanaged resources here, if any...

                _disposed = true;
            }
        }

        public ServiceState(ILogger<RemoteWebViewService> logger, bool enableMirrors)
        {
            EnableMirrors = enableMirrors;
            CancellationTokenSource = new CancellationTokenSource();
            Token = CancellationTokenSource.Token;
            IPC = new IPC(Token,logger,EnableMirrors);
            Logger = logger;           
        }
    }
   
}
