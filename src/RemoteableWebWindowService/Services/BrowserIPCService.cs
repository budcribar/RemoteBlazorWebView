using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading;
using RemoteableWebWindowService;
using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using RemoteableWebWindowService.Services;

namespace PeakSwc.RemoteableWebWindows
{
    public class BrowserIPCService : BrowserIPC.BrowserIPCBase
    {
        private readonly ILogger<RemoteWebWindowService> _logger;
        private ConcurrentDictionary<string, ServiceState> IPC { get; set; }
        private ConcurrentDictionary<string, BrowserIPCState> StateDict { get; init; }
        private volatile bool shutdown = false;
       
        public BrowserIPCService(ILogger<RemoteWebWindowService> logger, ConcurrentDictionary<string, ServiceState> ipc, ConcurrentDictionary<string, BrowserIPCState> state)
        {
            _logger = logger;         
            IPC = ipc;
            StateDict = state;
        }

        public void Shutdown()
        {
            // TODO Need to call this at some point
            _logger.LogInformation("Shutting down.");
            shutdown = true;
        }

        public override Task ReceiveMessage(IdMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            IPC[request.Id].IPC.BrowserResponseStream = responseStream;

            while (!shutdown)
                Thread.Sleep(1000);

            return Task.CompletedTask;
        }

        public override Task<Empty> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {         
            if (!StateDict.ContainsKey(request.Id)) StateDict.TryAdd(request.Id, new BrowserIPCState());

            var state = StateDict[request.Id];

            lock (state)
            {
                if (request.Sequence == state.SequenceNum)
                {
                    IPC[request.Id].IPC.ReceiveMessage(request.Message);
                    state.SequenceNum++;
                }
                else
                    state.MessageDictionary.TryAdd(request.Sequence, request);

                while (state.MessageDictionary.ContainsKey(state.SequenceNum))
                {
                    IPC[request.Id].IPC.ReceiveMessage(state.MessageDictionary[state.SequenceNum].Message);
                    state.SequenceNum++;
                }
            }

            return Task.FromResult<Empty>(new Empty());
        }
    }
}
