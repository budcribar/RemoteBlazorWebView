using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;

namespace PeakSWC.RemoteableWebView
{
    public class BrowserIPCService : BrowserIPC.BrowserIPCBase
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private ConcurrentDictionary<string, ServiceState> ServiceDictionary { get; set; }
        
        private volatile bool shutdown = false;
       
        public BrowserIPCService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> serviceDictionary)
        {
            _logger = logger;         
            ServiceDictionary = serviceDictionary;
        }

        public void Shutdown()
        {
            // TODO Need to call this at some point
            _logger.LogInformation("Shutting down.");
            shutdown = true;
        }

        public override Task ReceiveMessage(IdMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            ServiceDictionary[request.Id].IPC.BrowserResponseStream = responseStream;

            while (!shutdown)
                Thread.Sleep(1000);

            return Task.CompletedTask;
        }

        public override Task<Empty> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {
            var state = ServiceDictionary[request.Id]?.BrowserIPC;

            if (state == null)
                return Task.FromResult(new Empty());

            lock (state)
            {
                if (request.Sequence == state.SequenceNum)
                {
                    ServiceDictionary[request.Id].IPC.ReceiveMessage(request.Message);
                    state.SequenceNum++;
                }
                else
                    state.MessageDictionary.TryAdd(request.Sequence, request);

                while (state.MessageDictionary.ContainsKey(state.SequenceNum))
                {
                    ServiceDictionary[request.Id].IPC.ReceiveMessage(state.MessageDictionary[state.SequenceNum].Message);
                    state.SequenceNum++;
                }
            }

            return Task.FromResult(new Empty());
        }
    }
}
