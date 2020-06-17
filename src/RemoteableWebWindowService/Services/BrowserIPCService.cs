using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading;
using RemoteableWebWindowService;
using System;
using System.Collections.Concurrent;

namespace PeakSwc.RemoteableWebWindows
{
    public class BrowserIPCService : BrowserIPC.BrowserIPCBase
    {
        private readonly ILogger<RemoteWebWindowService> _logger;
        public ConcurrentDictionary<Guid, IPC> IPC { get; set; }
        private volatile bool shutdown = false;

        public BrowserIPCService(ILogger<RemoteWebWindowService> logger, ConcurrentDictionary<Guid, IPC> ipc)
        {
            _logger = logger;
            
            IPC = ipc;
        }

        public void Shutdown()
        {
            _logger.LogInformation("Shutting down.");
            shutdown = true;
        }

        public override Task ReceiveMessage(IdMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            Guid id = Guid.Parse(request.Id);
            if (!IPC.ContainsKey(id)) IPC.TryAdd(id,new IPC());
            IPC[id].BrowserResponseStream = responseStream;

            while (!shutdown)
                Thread.Sleep(1000);

            return Task.CompletedTask;
        }

        public override Task<EmptyRequest> SendMessage(StringRequest request, ServerCallContext context)
        {
            Guid id = Guid.Parse(request.Id);
            if (!IPC.ContainsKey(id)) IPC.TryAdd(id, new IPC());
            IPC[id].ReceiveMessage(request.Request);
            return Task.FromResult<EmptyRequest>(new EmptyRequest());
        }


    }
}
