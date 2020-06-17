using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace RemoteableWebWindowService.Services
{
    public class ServiceState
    {
        public string HtmlHostPath { get; set; }
        public string Title { get; set; }
        public string Hostname { get; set; }
        public bool InUse { get; set; }
        public ConcurrentDictionary<string, (MemoryStream stream, ManualResetEventSlim mres)> FileDictionary { get; set; } = new ConcurrentDictionary<string, (MemoryStream stream, ManualResetEventSlim mres)>();
        public Channel<string> FileCollection { get; set; } = Channel.CreateUnbounded<string>();

        public ServiceState()
        {

        }
    }

   
}
