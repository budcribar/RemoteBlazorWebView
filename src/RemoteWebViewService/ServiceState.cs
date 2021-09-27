﻿
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
        public string User {  get; set; } = string.Empty;
        public Task? FileReaderTask { get; set; }
        public ConcurrentDictionary<string, (MemoryStream stream, ManualResetEventSlim resetEvent)> FileDictionary { get; set; } = new();
        public Channel<string> FileCollection { get; set; } = Channel.CreateUnbounded<string>();
        public IPC IPC { get; init; } = new();
        public BrowserIPCState BrowserIPC { get; init; } = new();
    }
}