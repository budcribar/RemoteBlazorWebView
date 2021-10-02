using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;


namespace PeakSWC.RemoteWebView
{
    public class ClientIPCService : ClientIPC.ClientIPCBase
    {
        private readonly ILogger<ClientIPCService> _logger;
        private readonly ConcurrentDictionary<string,Channel<ClientResponseList>> _serviceStateChannel;
        private readonly ConcurrentDictionary<string, ServiceState> _rootDictionary;
        private readonly ProtectedApiCallHelper _graphApi;

        private Dictionary<string, string> GetGroups(JObject result)
        {
            Dictionary<string, string> groups = new();
            var list = result.Property("value")?.Value;
            if (list != null)
                foreach (var group in list)
                {
                    var name = group["displayName"]?.ToString() ?? string.Empty;
                    var id = group["id"]?.ToString() ?? string.Empty;

                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                        continue;

                    groups[id] = name;
                }
            return groups;
        }

        private List<string> GetMembersForGroup(string groupId, string userId, Dictionary<string, string> groupDict, JObject result)
        {
            List<string> results = new();
            var list = result.Property("value")?.Value;
            if (list != null)
                foreach (var members in list)
                {

                    var id = members["id"]?.ToString() ?? string.Empty;
                    if (id == userId)
                        results.Add(groupDict[groupId]);
                }
            return results;
        }

        public ClientIPCService(ILogger<ClientIPCService> logger, ConcurrentDictionary<string,Channel<ClientResponseList>> serviceStateChannel, ConcurrentDictionary<string, ServiceState> rootDictionary, ProtectedApiCallHelper graphApi)
        {
            _logger = logger;
            _serviceStateChannel = serviceStateChannel;
            _rootDictionary = rootDictionary;
            _graphApi = graphApi;
        }

        public override async Task GetClients(UserMessageRequest request, IServerStreamWriter<ClientResponseList> responseStream, ServerCallContext context)
        {
            string id = context.GetHttpContext().Connection.Id;
            _serviceStateChannel.TryAdd(id, Channel.CreateUnbounded<ClientResponseList>());
            // https://stackoverflow.com/questions/48385996/platformnotsupported-exception-when-calling-adduserasync-net-core-2-0
            var list = new ClientResponseList();
            try
            {
                var groups = GetUserGroups(request.Oid);

                // If a user is not in any groups then they are defaulted to the "test" group
                if (!groups.Any())
                    groups.Add("test");

                _rootDictionary.Values.Where(x => groups.Contains(x.Group)).ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { Markup = x.Markup, Id = x.Id, State = x.InUse ? ClientState.Connected : ClientState.ShuttingDown, Url = x.Url, Group = x.Group }));
                await responseStream.WriteAsync(list);

                await foreach (var state in _serviceStateChannel[id].Reader.ReadAllAsync())
                {
                    await responseStream.WriteAsync(state);
                }
            }
            finally 
            {
                _serviceStateChannel.Remove(id, out _);
            }
           
        }

        private List<string> GetUserGroups (string oid)
        {
            List<string> groups = new();
            var groupText = _graphApi.CallWebApiAndProcessResultASync($"https://graph.microsoft.com/v1.0/groups").Result;
            if (groupText == null) { return groups; }
            var groupDict = GetGroups(groupText);

            foreach (var groupId in groupDict.Keys)
            {
                var members = _graphApi.CallWebApiAndProcessResultASync($"https://graph.microsoft.com/v1.0/groups/" + groupId + $"/members").Result;
                if (members != null)
                    groups.AddRange(GetMembersForGroup(groupId, oid, groupDict, members));
            }
            return groups;
        }
    }
}
