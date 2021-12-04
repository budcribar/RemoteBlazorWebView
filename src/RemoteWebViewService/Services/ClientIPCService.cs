using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Channels;
using System.Threading.Tasks;


namespace PeakSWC.RemoteWebView
{
    public class ClientIPCService : ClientIPC.ClientIPCBase
    {
        private readonly ILogger<ClientIPCService> _logger;
        private readonly ConcurrentDictionary<string,Channel<string>> _serviceStateChannel;
        private ConcurrentDictionary<string, ServiceState> ServiceDictionary { get; init; }
        private readonly IUserService _userService;
       
        public ClientIPCService(ILogger<ClientIPCService> logger, ConcurrentDictionary<string,Channel<string>> serviceStateChannel, ConcurrentDictionary<string, ServiceState> serviceDictionary, IUserService userService)
        {
            _logger = logger;
            _serviceStateChannel = serviceStateChannel;
            ServiceDictionary = serviceDictionary;
            _userService = userService;
        }

        public override async Task GetClients(UserMessageRequest request, IServerStreamWriter<ClientResponseList> responseStream, ServerCallContext context)
        {
            string id = request.Id;
            _serviceStateChannel.TryAdd(id, Channel.CreateUnbounded<string>());
            // https://stackoverflow.com/questions/48385996/platformnotsupported-exception-when-calling-adduserasync-net-core-2-0
           
            try
            {
                var groups = await _userService.GetUserGroups(request.Oid);
                
                // If a user is not in any groups then they are defaulted to the "test" group
                if (!groups.Any())
                    groups.Add("test");

                await WriteResponse(responseStream, groups);

                await foreach (var state in _serviceStateChannel[id].Reader.ReadAllAsync(context.CancellationToken))
                {
                    this._logger.LogInformation($"Client IPC: {state}");
                    await WriteResponse(responseStream, groups);
                }
            }
            finally 
            {
                _serviceStateChannel.Remove(id, out _);
            }        
        }

        private Task WriteResponse(IServerStreamWriter<ClientResponseList> responseStream, List<string> groups)
        {
            var list = new ClientResponseList();
            list.ClientResponses.AddRange(ServiceDictionary.Values.Where(x => groups.Contains(x.Group)).Select(x => new ClientResponse { Markup = x.Markup, Id = x.Id, State = x.InUse ? ClientState.Connected : ClientState.ShuttingDown, Url = x.Url, Group = x.Group }));
            return responseStream.WriteAsync(list);
        }
    }
}
