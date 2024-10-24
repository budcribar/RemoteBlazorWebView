using Grpc.Core;
using Microsoft.Extensions.Logging;
#if AUTHORIZATION
using Newtonsoft.Json;
using System.Linq;
#endif
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;


namespace PeakSWC.RemoteWebView
{
    public class BrowserIPCService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, ShutdownService shutdownService) : BrowserIPC.BrowserIPCBase
    {
        public override async Task ReceiveMessage(ReceiveMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            var serviceStateTaskSource = serviceDictionary.GetOrAdd(request.Id, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

            try
            {
                var serviceState = await serviceStateTaskSource.Task.WaitWithTimeout(TimeSpan.FromSeconds(60)).ConfigureAwait(false); 
                using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);
                serviceState.IPC.BrowserResponseStream(new BrowserResponseNode(responseStream, request.ClientId, request.IsPrimary), linkedToken);
                try
                {
                    while (!linkedToken.Token.IsCancellationRequested)
                        await Task.Delay(30).ConfigureAwait(false);

                }
                catch (Exception ex)
                {
                    if (request.IsPrimary)
                    {
                        await shutdownService.Shutdown(request.Id, ex).ConfigureAwait(false);
                    }
                    return;
                }

                if (request.IsPrimary)
                {
                    await shutdownService.Shutdown(request.Id).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                if (request.IsPrimary)
                    await shutdownService.Shutdown(request.Id).ConfigureAwait(false);
            }

            return;
        }

        public override async Task<SendMessageResponse> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {
            var serviceStateTaskSource = serviceDictionary.GetOrAdd(request.Id, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));
            try
            {
               
                var serviceState = await serviceStateTaskSource.Task.WaitWithTimeout(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);
                // Skip messages from read only client
                if (!request.IsPrimary)
                {
                    logger.LogDebug($"Skipped send message {request.Message} from connection {request.ClientId}");
                    return new SendMessageResponse { Id = request.Id, Success = true };
                }

                var state = serviceState.BrowserIPC;
                if (state == null)
                    return new SendMessageResponse { Id = request.Id, Success = false };

                try
                {
                    await state.Semaphore.WaitAsync(linkedToken.Token).ConfigureAwait(false);

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
                        await serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = request.Message, Url = request.Url, Cookies = request.Cookies }).ConfigureAwait(false);
                        state.SequenceNum++;
                    }
                    else
                    {
                        bool added = state.MessageDictionary.TryAdd(request.Sequence, request);
                        if (!added)
                        {
                            logger.LogError("Failed to add message to dictionary for Sequence: {Sequence}, Id: {Id}", request.Sequence, request.Id);
                        }
                    }

                    while (state.MessageDictionary.TryRemove(state.SequenceNum, out var queuedRequest))
                    {
                        await serviceState.IPC.ReceiveMessage(new WebMessageResponse
                        {
                            Response = queuedRequest.Message,
                            Url = queuedRequest.Url,
                            Cookies = queuedRequest.Cookies
                        }).ConfigureAwait(false);
                        state.SequenceNum++;
                    }
                }
                finally
                {
                    state.Semaphore.Release();
                }

                return new SendMessageResponse { Id = request.Id, Success = true };
            }
            catch {
                return new SendMessageResponse { Id = request.Id, Success = false };
            }
        
        }
    }
}
