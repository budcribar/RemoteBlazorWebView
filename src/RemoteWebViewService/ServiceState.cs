
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class FileEntry
    {
        public ManualResetEventSlim ResetEvent { get; set; } = new ManualResetEventSlim();
        public long Length { get; set; } = -1;
        public Pipe Pipe { get; set; } = new Pipe();
        public void Reset() { ResetEvent = new ManualResetEventSlim(); Length = -1; Pipe = new Pipe(); }
    }

    public class ServiceState : IDisposable
    {
        public IRequestCookieCollection Cookies { get; set; }
        private CancellationTokenSource CancellationTokenSource { get; }
        public ILogger<RemoteWebViewService> Logger;
        public CancellationToken Token { get; }
        public string HtmlHostPath { get; init; } = string.Empty;
        public string Markup { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public bool InUse { get; set; } = false;
        public bool Refresh { get; set; } = false;
        public string Id { get; init; } = string.Empty;
        public string ClientId { get; set; } = string.Empty;
        public ConcurrentBag<string> ConnectionId { get; set; } = new();
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
        public ConcurrentDictionary<string, FileEntry> FileDictionary { get; set; } = new();
        public Channel<string> FileCollection { get; set; } = Channel.CreateUnbounded<string>();
        public IPC IPC { get; }
        public BrowserIPCState BrowserIPC { get; init; } = new();

        public DateTime StartTime { get; } = DateTime.Now;
        public void Cancel()
        {
            CancellationTokenSource.Cancel();
        }
        public void Dispose()
        {
            CancellationTokenSource.Dispose();
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
