using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading;
using System;
using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using System.Threading.Channels;

namespace PeakSwc.RemoteableWebWindows
{
    public class ClientIPCService : ClientIPC.ClientIPCBase
    {
        private readonly ILogger<ClientIPCService> _logger;
        private readonly Channel<ClientResponseList> _serviceStateChannel;

        public ClientIPCService(ILogger<ClientIPCService> logger, Channel<ClientResponseList> serviceStateChannel)
        {
            _logger = logger;

            _serviceStateChannel = serviceStateChannel;
        }

        public override async Task GetClients(Empty request, IServerStreamWriter<ClientResponseList> responseStream, ServerCallContext context)
        {
            await foreach (var state in _serviceStateChannel.Reader.ReadAllAsync())
            {
                await responseStream.WriteAsync(state);
            }
        }


    }
}
