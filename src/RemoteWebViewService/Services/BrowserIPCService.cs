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
                // Must not be context.CancellationToken: a disconnecting browser would cancel this wait and shut down early.
                var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);

                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);

                serviceState.IPC.BrowserResponseStream(new BrowserResponseNode(responseStream, request.ClientId, request.IsPrimary),linkedToken);

                try
                {
                    await Task.Delay(Timeout.Infinite, linkedToken.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected exception when cancellation occurs, don't treat as error
                    // Fall through to shutdown if primary
                }
                catch (Exception ex)
                {
                    if (request.IsPrimary)
                    {
                        await shutdownService.Shutdown(request.Id, ex).ConfigureAwait(false);
                    }
                    return;
                }

                // Only shutdown if this is the primary connection
                if (request.IsPrimary)
                {
                    await shutdownService.Shutdown(request.Id).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (request.IsPrimary)
                {
                    await shutdownService.Shutdown(request.Id, ex).ConfigureAwait(false);
                }
            }
        }
  

        public override async Task<SendMessageResponse> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {
            // A new primary page starts at sequence 1 and may arrive before CreateWebView.
            // Later messages, including a dying WebView after deregister, must not create an entry.
            TaskCompletionSource<ServiceState> serviceStateTaskSource;
            if (request.IsPrimary && request.Sequence == 1)
            {
                serviceStateTaskSource = serviceDictionary.GetOrAdd(request.Id, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));
            }
            else if (!serviceDictionary.TryGetValue(request.Id, out var existing) || existing == null)
            {
                logger.LogDebug("Dropped browser SendMessage for client {Id} at sequence {Sequence}.", request.Id, request.Sequence);
                return new SendMessageResponse { Id = request.Id, Success = false };
            }
            else
            {
                serviceStateTaskSource = existing;
            }

            try
            {
                // Must not be context.CancellationToken: a disconnecting browser would cancel this wait and shut down early.
                var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
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
