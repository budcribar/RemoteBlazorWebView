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
        private ConcurrentDictionary<string, IPC> IPC { get; set; }
        private ConcurrentDictionary<string, BrowserIPCState> stateDict { get; init; }
        private volatile bool shutdown = false;
       
        public BrowserIPCService(ILogger<RemoteWebWindowService> logger, ConcurrentDictionary<string, IPC> ipc, ConcurrentDictionary<string, BrowserIPCState> state)
        {
            _logger = logger;         
            IPC = ipc;
            stateDict = state;
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

        public override Task<Empty> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {         
            if (!IPC.ContainsKey(request.Id)) IPC.TryAdd(request.Id, new IPC());
            if (!stateDict.ContainsKey(request.Id)) stateDict.TryAdd(request.Id, new BrowserIPCState());

            var state = stateDict[request.Id];

            lock (state)
            {
                if (request.Sequence == state.SequenceNum)
                {
                    IPC[request.Id].ReceiveMessage(request.Message);
                    state.SequenceNum++;
                }
                else
                    state.MessageDictionary.TryAdd(request.Sequence, request);

                while (state.MessageDictionary.ContainsKey(state.SequenceNum))
                {
                    IPC[request.Id].ReceiveMessage(state.MessageDictionary[state.SequenceNum].Message);
                    state.SequenceNum++;
                }
            }

            return Task.FromResult<Empty>(new Empty());
        }
    }
}
