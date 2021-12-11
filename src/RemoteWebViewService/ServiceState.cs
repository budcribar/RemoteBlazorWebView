﻿
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class ServiceState
    {
        public string HtmlHostPath { get; init; } = string.Empty;
        public string Markup { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public bool InUse { get; set; } = false;
        public string Id { get; init; } = string.Empty;
        public string Group { get; init;  } = string.Empty;
        public int Pid { get; init; } = 0;
        public string ProcessName {  get; init; } = string.Empty;
        public string HostName { get; init; } = string.Empty;
        public string User {  get; set; } = string.Empty;
        public Task? FileReaderTask { get; set; } = null;
        public Task? PingTask { get; set; } = null;
        public Task? BrowserPingTask { get; set; } = null;
        public DateTime BrowserPingReceived { get; set; } = DateTime.MinValue;
        public TimeSpan MaxBrowserPing { get; set; } = TimeSpan.Zero;
        public ConcurrentDictionary<string, (MemoryStream stream, ManualResetEventSlim resetEvent)> FileDictionary { get; set; } = new();
        public Channel<string> FileCollection { get; set; } = Channel.CreateUnbounded<string>();
        public IPC IPC { get; init; } = new();
        public BrowserIPCState BrowserIPC { get; init; } = new();
    }
}
