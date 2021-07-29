using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Azure.ActiveDirectory.GraphClient;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using System;
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
        private ActiveDirectoryClient _activeDirectoryClient;

        public ClientIPCService(ILogger<ClientIPCService> logger, Channel<ClientResponseList> serviceStateChannel, ConcurrentDictionary<string, ServiceState> rootDictionary, ActiveDirectoryClient activeDirectoryClient)
        {
            _logger = logger;
            _serviceStateChannel = serviceStateChannel;
            _rootDictionary = rootDictionary;
            _activeDirectoryClient = activeDirectoryClient;
        }

        public override async Task GetClients(UserMessageRequest request, IServerStreamWriter<ClientResponseList> responseStream, ServerCallContext context)
        {
            // https://stackoverflow.com/questions/48385996/platformnotsupported-exception-when-calling-adduserasync-net-core-2-0
            var list = new ClientResponseList();
            var user = _activeDirectoryClient.Users.GetByObjectId(request.Oid);
            
            var groups = (await user.GetMemberGroupsAsync(false)).ToList();

            // If a user is not in any groups then they are defaulted to the "test" group
            if (!groups.Any()) groups.Add("test");

            _rootDictionary.Values.Where(x => groups.Contains(x.Group)).ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { HostName = x.Hostname, Id = x.Id, State = x.InUse ? ClientState.ShuttingDown : ClientState.Connected, Url = x.Url }));
            await responseStream.WriteAsync(list);

            await foreach (var state in _serviceStateChannel.Reader.ReadAllAsync())
            {
                await responseStream.WriteAsync(state);
            }
        }


    }
}
