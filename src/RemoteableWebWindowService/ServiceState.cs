using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}
namespace RemoteableWebWindowService.Services
{
    public class ServiceState
    {
        public string HtmlHostPath { get; init; } = string.Empty;
        public string Hostname { get; init; } = string.Empty;
        public bool InUse { get; set; }
        public ConcurrentDictionary<string, (MemoryStream? stream, ManualResetEventSlim resetEvent)> FileDictionary { get; set; } = new ();
        public Channel<string> FileCollection { get; set; } = Channel.CreateUnbounded<string>();

        //public ServiceState()
        //{

        //}
    }

   
}
