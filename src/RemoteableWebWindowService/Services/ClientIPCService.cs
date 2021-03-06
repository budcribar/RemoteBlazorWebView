using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteableWebView
{
    public class ClientIPCService : ClientIPC.ClientIPCBase
    {
        private readonly ILogger<ClientIPCService> _logger;
        private readonly Channel<ClientResponseList> _serviceStateChannel;
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;

        public ClientIPCService(ILogger<ClientIPCService> logger, Channel<ClientResponseList> serviceStateChannel, ConcurrentDictionary<string, ServiceState> rootDictionary)
        {
            _logger = logger;

            _serviceStateChannel = serviceStateChannel;
            _rootDictionary = rootDictionary;
        }

        public override async Task GetClients(Empty request, IServerStreamWriter<ClientResponseList> responseStream, ServerCallContext context)
        {
            var list = new ClientResponseList();
            _rootDictionary.Values.ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { HostName = x.Hostname, Id = x.Id, State = x.InUse ? ClientState.ShuttingDown : ClientState.Connected, Url = x.Url }));
            await responseStream.WriteAsync(list);

            await foreach (var state in _serviceStateChannel.Reader.ReadAllAsync())
            {
                await responseStream.WriteAsync(state);
            }
        }


    }
}
