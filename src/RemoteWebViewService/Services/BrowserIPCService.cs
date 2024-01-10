using Grpc.Core;
using Microsoft.Extensions.Logging;
#if AUTHORIZATION
using Newtonsoft.Json;
#endif
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Linq;
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

        public override Task<ClientIdMessageRequest> GetClientId(IdMessageRequest request, ServerCallContext context)
        {
            if (!_serviceDictionary.TryGetValue(request.Id, out ServiceState? serviceState))
            {
                _shutdownService.Shutdown(request.Id);
                return Task.FromResult(new ClientIdMessageRequest());
            }

            var guid = Guid.NewGuid().ToString();
            var isPrimary = false;
            if (string.IsNullOrEmpty(serviceState.ClientId))
            {
                isPrimary = true;
                serviceState.ClientId = guid;
            }

            return Task.FromResult(new ClientIdMessageRequest { Id = guid, ClientId = guid, IsPrimary = isPrimary });
        }

        public override async Task ReceiveMessage(IdMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            if (!_serviceDictionary.TryGetValue(request.Id, out ServiceState? serviceState))
            {
                _shutdownService.Shutdown(request.Id);
                return;
            }


            using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);
            var isPrimary = serviceState.IPC.BrowserResponseStream(responseStream, linkedToken);
            try
            {
                while (!linkedToken.Token.IsCancellationRequested)
                    await Task.Delay(1000, linkedToken.Token).ConfigureAwait(false);

            }
            catch (Exception ex)
            {
                if (isPrimary && serviceState.ClientId == request.Id)
                    _shutdownService.Shutdown(request.Id, ex);
            }

            return;
        }

        public override Task<SendMessageResponse> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {
            if (!_serviceDictionary.TryGetValue(request.Id, out ServiceState? serviceState))
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = false });

            // Skip messages from read only client
            if (serviceState.ClientId != request.ClientId)
            {
                _logger.LogInformation($"Skipped send message {request.Message} from connection {request.ClientId}");
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = true });
            }

            var state = serviceState.BrowserIPC;

            if (state == null)
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = false });

            lock (state)
            {
                if (request.Sequence == state.SequenceNum)
                {
                    if (request.Message == "connected:")
                    {
                        request.Message += context.GetHttpContext().Connection.RemoteIpAddress + "|" + serviceState.User;
#if AUTHORIZATION
                        string serializedCookies = string.Empty;
                        if (serviceState.Cookies != null)
                            serializedCookies = JsonConvert.SerializeObject(serviceState.Cookies.ToDictionary(c => c.Key, c => c.Value));

                        request.Cookies = serializedCookies;
#endif
                    }

                    serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = request.Message, Url = request.Url, Cookies = request.Cookies }).GetAwaiter().GetResult();
                    state.SequenceNum++;
                }
                else
                    state.MessageDictionary.TryAdd(request.Sequence, request);

                while (state.MessageDictionary.ContainsKey(state.SequenceNum))
                {
                    serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = state.MessageDictionary[state.SequenceNum].Message, Url = state.MessageDictionary[state.SequenceNum].Url, Cookies = state.MessageDictionary[state.SequenceNum].Cookies }).GetAwaiter().GetResult();
                    state.SequenceNum++;
                }
            }

            return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = true });
        }
    }
}
