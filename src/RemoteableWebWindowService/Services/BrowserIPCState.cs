﻿using System.Collections.Concurrent;

namespace PeakSwc.RemoteableWebWindows
{
    public class BrowserIPCState
    {
        public ConcurrentDictionary<uint, SendSequenceMessageRequest> MessageDictionary { get; } = new();
        public uint SequenceNum { get; set;} = 1;
    }
}
