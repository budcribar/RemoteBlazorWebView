using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Services;
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
        private ShutdownService ShutdownService { get; }

        public BrowserIPCService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> serviceDictionary, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ShutdownService shutdownService)
        {
            _logger = logger;
            ServiceDictionary = serviceDictionary;
            _serviceStateChannel = serviceStateChannel;
            ShutdownService = shutdownService;
        }

        public override async Task ReceiveMessage(IdMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            if (!ServiceDictionary.TryGetValue(request.Id, out ServiceState? serviceState))
            {
                ShutdownService.Shutdown(request.Id);
                return;
            }

            serviceState.IPC.BrowserResponseStream = responseStream;

            while (!serviceState.Token.IsCancellationRequested)
                await Task.Delay(1000, serviceState.Token).ConfigureAwait(false);

            return;
        }

        public override Task<SendMessageResponse> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {
            if (!ServiceDictionary.TryGetValue(request.Id, out ServiceState? serviceState))
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = false });

            var state = serviceState.BrowserIPC;

            if (state == null)
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = false });

            lock (state)
            {
                if (request.Sequence == state.SequenceNum)
                {
                    serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = request.Message, Url = request.Url });
                    state.SequenceNum++;
                }
                else
                    state.MessageDictionary.TryAdd(request.Sequence, request);

                while (state.MessageDictionary.ContainsKey(state.SequenceNum))
                {
                    serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = state.MessageDictionary[state.SequenceNum].Message, Url = state.MessageDictionary[state.SequenceNum].Url });
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
                if (ServiceDictionary.TryGetValue(id, out ServiceState? serviceState))
                {

                    if (serviceState.BrowserPingReceived != DateTime.MinValue)
                    {
                        var delta = DateTime.Now.Subtract(serviceState.BrowserPingReceived);
                        if (serviceState.MaxBrowserPing < delta)
                            serviceState.MaxBrowserPing = delta;
                        _logger.LogInformation($"Max Ping Response from browser id {id} {serviceState.MaxBrowserPing}");
                    }

                    serviceState.BrowserPingReceived = DateTime.Now;

                    if (message.Initialize)
                    {
                        serviceState.BrowserPingTask = Task.Factory.StartNew(async () =>
                        {
                            while (!serviceState.Token.IsCancellationRequested)
                            {
                                await Task.Delay(TimeSpan.FromSeconds(1), serviceState.Token).ConfigureAwait(false);
                                TimeSpan delta = DateTime.Now.Subtract(serviceState.BrowserPingReceived);
                                    // Account for high utilization
                                    if (delta > TimeSpan.FromSeconds(message.PingIntervalSeconds * 1.5))
                                {
                                    ShutdownService.Shutdown(id, new Exception($"Browser Ping Timed out in {delta}"));
                                    break;
                                }

                            }
                        }, serviceState.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }
                    return Task.FromResult(new PingMessageResponse { Id = id, Cancelled = false });
                }
                else
                   return Task.FromResult(new PingMessageResponse { Id = id, Cancelled = true });
            }
            catch (Exception ex)
            {
                ShutdownService.Shutdown(id, ex);
                return Task.FromResult(new PingMessageResponse { Id = id, Cancelled = true });
            }
        }

    }
}
