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
        public ConcurrentDictionary<string, IPC> IPC { get; set; }
        private volatile bool shutdown = false;

        public BrowserIPCService(ILogger<RemoteWebWindowService> logger, ConcurrentDictionary<string, IPC> ipc)
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
            if (!IPC.ContainsKey(request.Id)) IPC.TryAdd(request.Id, new IPC());
            IPC[request.Id].BrowserResponseStream = responseStream;

            while (!shutdown)
                Thread.Sleep(1000);

            return Task.CompletedTask;
        }

        // TODO Use Google EmptyRequest
        public override Task<EmptyRequest> SendMessage(StringRequest request, ServerCallContext context)
        {
            if (!IPC.ContainsKey(request.Id)) IPC.TryAdd(request.Id, new IPC());
            IPC[request.Id].ReceiveMessage(request.Request);
            return Task.FromResult<EmptyRequest>(new EmptyRequest());
        }

    }
}
