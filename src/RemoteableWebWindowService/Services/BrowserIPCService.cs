using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading;
using RemoteableWebWindowService;
using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;

namespace PeakSwc.RemoteableWebWindows
{
    public class BrowserIPCService : BrowserIPC.BrowserIPCBase
    {
        private readonly ILogger<RemoteWebWindowService> _logger;
        public ConcurrentDictionary<string, IPC> IPC { get; set; }
        private volatile bool shutdown = false;

        public BrowserIPCService(ILogger<RemoteWebWindowService> logger, ConcurrentDictionary<string, IPC> ipc)
        {
            _logger = logger;         
            IPC = ipc;
        }

        public void Shutdown()
        {
            // TODO Need to call this at some point
            _logger.LogInformation("Shutting down.");
            shutdown = true;
        }

        public override Task ReceiveMessage(IdMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
           
            if (!IPC.ContainsKey(request.Id)) IPC.TryAdd(request.Id, new IPC());
            IPC[request.Id].BrowserResponseStream = responseStream;

            while (!shutdown)
                Thread.Sleep(1000);

            return Task.CompletedTask;
        }

        public override Task<Empty> SendMessage(StringRequest request, ServerCallContext context)
        {
            if (!IPC.ContainsKey(request.Id)) IPC.TryAdd(request.Id, new IPC());
            IPC[request.Id].ReceiveMessage(request.Request);
            return Task.FromResult<Empty>(new Empty());
        }
    }
}
