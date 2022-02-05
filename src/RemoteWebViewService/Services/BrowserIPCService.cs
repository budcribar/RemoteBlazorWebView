using Grpc.Core;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class BrowserIPCService : BrowserIPC.BrowserIPCBase
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private readonly ConcurrentDictionary<string, ServiceState> _serviceDictionary;
        private readonly ShutdownService _shutdownService;

        public BrowserIPCService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> serviceDictionary, ShutdownService shutdownService)
        {
            _logger = logger;
            _serviceDictionary = serviceDictionary;
            _shutdownService = shutdownService;
        }

        public override async Task ReceiveMessage(IdMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            if (!_serviceDictionary.TryGetValue(request.Id, out ServiceState? serviceState))
            {
                _shutdownService.Shutdown(request.Id);
                return;
            }

            serviceState.IPC.BrowserResponseStream = responseStream;
            using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);
            try
            {
                while (!linkedToken.Token.IsCancellationRequested)
                    await Task.Delay(1000, linkedToken.Token).ConfigureAwait(false);

            }
            catch (Exception ex)
            {
                _shutdownService.Shutdown(request.Id, ex);
            }

            return;
        }

        public override Task<SendMessageResponse> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {
            if (!_serviceDictionary.TryGetValue(request.Id, out ServiceState? serviceState))
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = false });

            var state = serviceState.BrowserIPC;

            if (state == null)
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = false });

            lock (state)
            {
                if (request.Sequence == state.SequenceNum)
                {
                    if (request.Message == "connected:")
                        request.Message += context.GetHttpContext().Connection.RemoteIpAddress + ":" + serviceState.User;

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
    }
}
