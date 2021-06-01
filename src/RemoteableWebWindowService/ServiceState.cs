using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
namespace System.Runtime.CompilerServices
{
    // TODO This is a bug in compiler
    public class IsExternalInit { }
}
namespace RemoteableWebWindowService.Services
{
    public class ServiceState
    {
        public string HtmlHostPath { get; init; } = string.Empty;
        public string Hostname { get; init; } = string.Empty;
        public string Url { get; init; } = string.Empty;
        public bool InUse { get; set; }
        public string Id { get; init; } = string.Empty;
        public ConcurrentDictionary<string, (MemoryStream? stream, ManualResetEventSlim resetEvent)> FileDictionary { get; set; } = new ();
        public Channel<string> FileCollection { get; set; } = Channel.CreateUnbounded<string>();
    }
}
