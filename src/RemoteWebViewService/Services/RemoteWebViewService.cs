using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class RemoteWebViewService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> serviceDictionary, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ShutdownService shutdownService) : WebViewIPC.WebViewIPCBase
    {
        public override Task<IdArrayResponse> GetIds(Empty request, ServerCallContext context)
        {
            var results = new IdArrayResponse();
            results.Responses.AddRange(serviceDictionary.Keys);
            return Task.FromResult(results);
        }

        public override async Task CreateWebView(CreateWebViewRequest request, IServerStreamWriter<WebMessageResponse> responseStream, ServerCallContext context)
        {         
            logger.LogInformation($"CreateWebView Id:{request.Id}");

            ServiceState state= new(logger, request.EnableMirrors)
            {
                HtmlHostPath = request.HtmlHostPath,
                Markup = request.Markup,
                InUse = false,
                Url = $"https://{context.Host}/app/{request.Id}",
                Id = request.Id,
                Group = request.Group,
                Pid = request.Pid,
                HostName = request.HostName,
                ProcessName = request.ProcessName,
            };

            if (serviceDictionary.TryAdd(request.Id, state))
            {
                // No need to wait for writes to complete
                serviceStateChannel.Values.ToList().ForEach(async x => await x.Writer.WriteAsync($"Start:{request.Id}").ConfigureAwait(false));
                state.IPC.ClientResponseStream = responseStream;

                await responseStream.WriteAsync(new WebMessageResponse { Response = "created:" }).ConfigureAwait(false);
            }
            else
            {
                logger.LogError($"CreateWebView Id:{request.Id} failed to add client");
                await responseStream.WriteAsync(new WebMessageResponse { Response = "createFailed:" }).ConfigureAwait(false);
                return;
            }
            

            using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, state.Token);
            try
            {
                await Task.Delay(Timeout.Infinite, linkedToken.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Expected when cancellation is requested; no action needed.
            }
            catch (Exception ex)
            {
                logger.LogError($"CreateWebView Id:{request.Id} failed {ex.Message}");
                await shutdownService.Shutdown(request.Id, ex).ConfigureAwait(false);
            }
         
        }

        public override async Task RequestClientFileRead(IAsyncStreamReader<ClientFileReadResponse> requestStream, IServerStreamWriter<ServerFileReadRequest> responseStream, ServerCallContext context)
        {
            var id = string.Empty;
            try
            {
                await foreach (ClientFileReadResponse message in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
                {
                    id = message.ClientId;

                    if (serviceDictionary.TryGetValue(id, out var serviceState))
                    {
                        if (serviceState.Token.IsCancellationRequested)
                            break;

                        if (message.ResponseCase == ClientFileReadResponse.ResponseOneofCase.Init && serviceState.FileReaderTask == null)
                        {
                            serviceState.FileReaderTask = Task.Run(async () =>
                            {
                                try
                                {
                                    while (!serviceState.Token.IsCancellationRequested)
                                    {
                                        var fileEntry = await serviceState.FileCollection.Reader.ReadAsync(serviceState.Token).ConfigureAwait(false);
                                        await responseStream.WriteAsync(new ServerFileReadRequest { ClientId = id, Path = fileEntry.Path, RequestId = fileEntry.Instance.ToString() }, serviceState.Token).ConfigureAwait(false);
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    // Handle cancellation if necessary
                                }
                                catch (Exception ex)
                                {
                                    await shutdownService.Shutdown(id, ex).ConfigureAwait(false);
                                }
                            }, serviceState.Token);
                        }

                        else if (message.ResponseCase == ClientFileReadResponse.ResponseOneofCase.Metadata)
                        {
                            if (serviceState.FileDictionary.TryGetValue(message.Path, out var concurrentList) && concurrentList.Count > int.Parse(message.RequestId) && int.Parse(message.RequestId) >= 0)
                            {
                                var fileEntry = concurrentList[int.Parse(message.RequestId)];
                                fileEntry.Length = message.Metadata.Length;
                                fileEntry.Semaphore.Release();
                            }
                            else
                            {
                                logger.LogError($"FileEntry not found for Path: {message.Path}, Instance: {message.RequestId}");
                                await shutdownService.Shutdown(id).ConfigureAwait(false);
                            }
                        }
                        else if(message.ResponseCase == ClientFileReadResponse.ResponseOneofCase.FileData)
                        {
                            var fileEntry = serviceState.FileDictionary[message.Path][int.Parse(message.RequestId)]; 
                            if (message.FileData.FileChunk.Length > 0)
                            {        
                                // TODO is there a limit on the Pipe write?
                                await fileEntry.Pipe.Writer.WriteAsync(message.FileData.FileChunk.ToByteArray(), serviceState.Token).ConfigureAwait(false);
                                // _ = fileEntry.Pipe.Writer.FlushAsync();
                            }
                            else
                            {
                                // Trigger the stream read                              
                                fileEntry.Pipe.Writer.Complete();
                            }
                        }
                    }

                    else break;
                }
            }
            catch (OperationCanceledException)
            {
                // No need to shutdown as we are in the process of shutting down
            }
            catch (Exception ex)
            {
                await shutdownService.Shutdown(id,ex).ConfigureAwait(false);
            }
        }

        public override async Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            await shutdownService.Shutdown(request.Id).ConfigureAwait(false);
            return new Empty();
        }

        public override async Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            if (serviceDictionary.TryGetValue(request.Id,out ServiceState? serviceState))          
			{
                await serviceState.IPC.SendMessage(request.Message, serviceState.Token).ConfigureAwait(false);

                return new SendMessageResponse { Id = request.Id, Success = true };
            }

            return new SendMessageResponse { Id = request.Id, Success = false };
        }

        public override async Task Ping(IAsyncStreamReader<PingMessageRequest> requestStream, IServerStreamWriter<PingMessageResponse> responseStream, ServerCallContext context)
        {
            var id = string.Empty;
            try
            {
                DateTime responseReceived = DateTime.UtcNow;
                DateTime responseSent = DateTime.UtcNow;

                await foreach (PingMessageRequest message in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
                {
                    id = message.Id;
                    if (!serviceDictionary.TryGetValue(id, out ServiceState? serviceState))
                    {
                        await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true }).ConfigureAwait(false);
                        await shutdownService.Shutdown(id).ConfigureAwait(false);
                        break;
                    }

                    if (message.Initialize && serviceState.PingTask == null)
                    {
                        serviceState.PingTask = Task.Run(async () =>
                        {
                            while (!serviceState.Token.IsCancellationRequested)
                            {
                                responseSent = DateTime.UtcNow;
                                await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = false }).ConfigureAwait(false);
                                await Task.Delay(TimeSpan.FromSeconds(message.PingIntervalSeconds), serviceState.Token).ConfigureAwait(false);
                                if (responseReceived < responseSent)
                                {
                                    await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true }).ConfigureAwait(false);
                                    await shutdownService.Shutdown(id).ConfigureAwait(false);
                                    break;
                                }

                            }
                        }, serviceState.Token);
                    }
                    else
                    {
                        responseReceived = DateTime.UtcNow;
                        var delta = responseReceived.Subtract(responseSent);
                        if (delta > serviceState.MaxClientPing)
                            serviceState.MaxClientPing = delta;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // No need to shutdown as we are in the process of shutting down
            }
            catch (Exception ex)
            {
                await shutdownService.Shutdown(id, ex).ConfigureAwait(false);
            }
        }
    }
}
