using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class BrowserIPCService : BrowserIPC.BrowserIPCBase
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private ConcurrentDictionary<string, ServiceState> ServiceDictionary { get; init; }
        private readonly ConcurrentDictionary<string, Channel<string>> _serviceStateChannel;
        private volatile bool shutdown = false;

        public BrowserIPCService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> serviceDictionary, ConcurrentDictionary<string, Channel<string>> serviceStateChannel)
        {
            _logger = logger;
            ServiceDictionary = serviceDictionary;
            _serviceStateChannel = serviceStateChannel;
        }

        private void ExShutdown(string id)
        {
            _logger.LogWarning("Shutting down..." + id);

            if (ServiceDictionary.ContainsKey(id))
            {
                ServiceDictionary.Remove(id, out var client);
                if (client != null)
                {
                    client.IPC.Shutdown();
                    client.InUse = false;
                }
            }
            _serviceStateChannel.Values.ToList().ForEach(x => x.Writer.TryWrite($"Shutdown:{id}"));
            shutdown = true;
        }

        public void Shutdown()
        {
            // TODO Need to call this at some point
            _logger.LogWarning("Shutting down.");
            shutdown = true;
        }

        public override async Task ReceiveMessage(IdMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            ServiceDictionary[request.Id].IPC.BrowserResponseStream = responseStream;

            try
            {
                while (!shutdown)
                    await Task.Delay(1000).ConfigureAwait(false);
            }
            catch (Exception)
            {
                ExShutdown(request.Id);
            }

            return;
        }

        public override Task<SendMessageResponse> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {
            if(!ServiceDictionary.ContainsKey(request.Id))
                return Task.FromResult(new SendMessageResponse { Id=request.Id, Success=false});

            var state = ServiceDictionary[request.Id]?.BrowserIPC;

            if (state == null)
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = false });

            lock (state)
            {
                if (request.Sequence == state.SequenceNum)
                {
                    ServiceDictionary[request.Id].IPC.ReceiveMessage(new WebMessageResponse { Response = request.Message, Url = request.Url });
                    state.SequenceNum++;
                }
                else
                    state.MessageDictionary.TryAdd(request.Sequence, request);

                while (state.MessageDictionary.ContainsKey(state.SequenceNum))
                {
                    ServiceDictionary[request.Id].IPC.ReceiveMessage(new WebMessageResponse { Response = state.MessageDictionary[state.SequenceNum].Message, Url = state.MessageDictionary[state.SequenceNum].Url });
                    state.SequenceNum++;
                }
            }

            return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = true });
        }

       
        public override Task<PingMessageResponse> Ping(PingMessageRequest message, ServerCallContext context)
        {
            var id = message.Id;
            try
            {
                if (ServiceDictionary[id].BrowserPingReceived != DateTime.MinValue)
                {
                    var delta = DateTime.Now.Subtract(ServiceDictionary[id].BrowserPingReceived);
                    if (ServiceDictionary[id].MaxBrowserPing < delta)
                        ServiceDictionary[id].MaxBrowserPing = delta;
                    _logger.LogInformation($"Max Ping Response from browser {delta}");
                }
                  

                ServiceDictionary[id].BrowserPingReceived = DateTime.Now;

                if (message.Initialize)
                {
                    ServiceDictionary[id].BrowserPingTask = Task.Run(async () =>
                    {
                        while (true)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                                
                            // Account for high utilization
                            if (DateTime.Now.Subtract(ServiceDictionary[id].BrowserPingReceived) > TimeSpan.FromSeconds(message.PingIntervalSeconds + 120))
                            {
                                ExShutdown(id);
                                break;
                            }

                        }
                    }, context.CancellationToken);
                }
              
                return Task.FromResult(new PingMessageResponse { Id = id, Cancelled = false });
            }
            catch (Exception)
            {
                ExShutdown(id);

                return Task.FromResult(new PingMessageResponse { Id = id, Cancelled = true });
            }
        }

    }
}
