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
    public class RemoteWebViewService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ShutdownService shutdownService, ServerFileSyncManager _fileSyncManager) : WebViewIPC.WebViewIPCBase
    {
        public override Task<IdArrayResponse> GetIds(Empty request, ServerCallContext context)
        {
            var results = new IdArrayResponse();
            results.Responses.AddRange(serviceDictionary.Keys);
            return Task.FromResult(results);
        }

        public override async Task CreateWebView(CreateWebViewRequest request, IServerStreamWriter<WebMessageResponse> responseStream, ServerCallContext context)
        {         
            logger.LogDebug($"CreateWebView Id:{request.Id}");

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

            var serviceStateTaskSource = serviceDictionary.GetOrAdd(request.Id, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

            if (serviceStateTaskSource.Task.IsCompleted)
            {
                logger.LogError($"CreateWebView Id:{request.Id} failed to add client");
                await responseStream.WriteAsync(new WebMessageResponse { Response = "createFailed:" }).ConfigureAwait(false);
                return;
            }     

            // No need to wait for writes to complete
            serviceStateChannel.Values.ToList().ForEach(async x => await x.Writer.WriteAsync($"Start:{request.Id}").ConfigureAwait(false));
            state.IPC.ClientResponseStream = responseStream;

            await responseStream.WriteAsync(new WebMessageResponse { Response = "created:" }).ConfigureAwait(false);

            serviceStateTaskSource.SetResult(state);

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

        //public override async Task RequestClientFileRead(IAsyncStreamReader<ClientFileReadResponse> requestStream, IServerStreamWriter<ServerFileReadRequest> responseStream, ServerCallContext context)
        //{
        //    var id = string.Empty;
        //    try
        //    {
        //        await foreach (ClientFileReadResponse message in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
        //        {
        //            id = message.ClientId;

        //            if (serviceDictionary.TryGetValue(id, out var serviceState))
        //            {
        //                if (serviceState.Token.IsCancellationRequested)
        //                    break;

        //                if (message.ResponseCase == ClientFileReadResponse.ResponseOneofCase.Init && serviceState.FileReaderTask == null)
        //                {
        //                    serviceState.FileReaderTask = Task.Run(async () =>
        //                    {
        //                        try
        //                        {
        //                            while (!serviceState.Token.IsCancellationRequested)
        //                            {
        //                                var fileEntry = await serviceState.FileCollection.Reader.ReadAsync(serviceState.Token).ConfigureAwait(false);
        //                                await responseStream.WriteAsync(new ServerFileReadRequest { ClientId = id, Path = fileEntry.Path, RequestId = fileEntry.Instance.ToString() }, serviceState.Token).ConfigureAwait(false);
        //                            }
        //                        }
        //                        catch (OperationCanceledException)
        //                        {
        //                            // Handle cancellation if necessary
        //                        }
        //                        catch (Exception ex)
        //                        {
        //                            await shutdownService.Shutdown(id, ex).ConfigureAwait(false);
        //                        }
        //                    }, serviceState.Token);
        //                }

        //                else if (message.ResponseCase == ClientFileReadResponse.ResponseOneofCase.Metadata)
        //                {
        //                    if (serviceState.FileDictionary.TryGetValue(message.Path, out var concurrentList) && concurrentList.Count > int.Parse(message.RequestId) && int.Parse(message.RequestId) >= 0)
        //                    {
        //                        var fileEntry = concurrentList[int.Parse(message.RequestId)];
        //                        fileEntry.Length = message.Metadata.Length;
        //                        fileEntry.Semaphore.Release();

        //                        // send a FileReadData request
        //                    }
        //                    else
        //                    {
        //                        logger.LogError($"FileEntry not found for Path: {message.Path}, Instance: {message.RequestId}");
        //                        await shutdownService.Shutdown(id).ConfigureAwait(false);
        //                    }
        //                }
        //                else if(message.ResponseCase == ClientFileReadResponse.ResponseOneofCase.FileData)
        //                {
        //                    var fileEntry = serviceState.FileDictionary[message.Path][int.Parse(message.RequestId)]; 
        //                    if (message.FileData.FileChunk.Length > 0)
        //                    {        
        //                        // TODO is there a limit on the Pipe write?
        //                        await fileEntry.Pipe.Writer.WriteAsync(message.FileData.FileChunk.ToByteArray(), serviceState.Token).ConfigureAwait(false);
        //                        // _ = fileEntry.Pipe.Writer.FlushAsync();
        //                    }
        //                    else
        //                    {
        //                        // Trigger the stream read                              
        //                        fileEntry.Pipe.Writer.Complete();
        //                    }
        //                }
        //            }

        //            else break;
        //        }
        //    }
        //    catch (OperationCanceledException)
        //    {
        //        // No need to shutdown as we are in the process of shutting down
        //    }
        //    catch (Exception ex)
        //    {
        //        await shutdownService.Shutdown(id,ex).ConfigureAwait(false);
        //    }
        //}

        public override async Task RequestClientFileRead(IAsyncStreamReader<ClientFileReadResponse> requestStream, IServerStreamWriter<ServerFileReadRequest> responseStream, ServerCallContext context)
        {
            // Handle Init message
            if (!await requestStream.MoveNext().ConfigureAwait(false))
            {
                logger.LogWarning("Client disconnected without sending Init message.");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Init message not received."));
            }

            var initResponse = requestStream.Current;

            if (initResponse.Init == null)
            {
                logger.LogWarning("First message from client is not Init.");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "First message must be Init."));
            }

            string clientGuid = initResponse.ClientId;
            logger.LogDebug($"Client '{clientGuid}' connected and initialized.");

            // Register the client
            _fileSyncManager.RegisterClient(clientGuid, initResponse.Init.HtmlHostPath);

            // Associate the response stream with the clientGuid
            _fileSyncManager.AssociateResponseStream(clientGuid, responseStream);



            try
            {
                var serviceStateTaskSource = serviceDictionary.GetOrAdd(clientGuid, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

                var serviceState = await serviceStateTaskSource.Task.WaitWithTimeout(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                serviceState.FileManagerReady.SetResult(true);

                // Create a linked cancellation token to handle both client cancellation and server cancellation
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);
                var cancellationToken = linkedCts.Token;


                // Continuously read messages from the client until cancellation
                while (await requestStream.MoveNext(cancellationToken).ConfigureAwait(false))
                {
                    var response = requestStream.Current;
                    await _fileSyncManager.HandleClientResponse(response).ConfigureAwait(false);
                }

                logger.LogDebug($"Client '{clientGuid}' has completed sending messages.");

            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Error handling client '{clientGuid}' responses.");
            }
            finally
            {
                // Clean up when the client disconnects
                _fileSyncManager.RemoveClient(clientGuid);
                logger.LogDebug($"Cleaned up resources for client '{clientGuid}'.");
            }
        }
        public override async Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            await shutdownService.Shutdown(request.Id).ConfigureAwait(false);
            return new Empty();
        }

        public override async Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            var serviceStateTaskSource = serviceDictionary.GetOrAdd(request.Id.ToString(), _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

            try
            {
                var serviceState = await serviceStateTaskSource.Task.WaitWithTimeout(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                await serviceState.IPC.SendMessage(request.Message).ConfigureAwait(false);
                return new SendMessageResponse { Id = request.Id, Success = true };
            }
            catch { }

            return new SendMessageResponse { Id = request.Id, Success = false };
        }

        public override async Task Ping(IAsyncStreamReader<PingMessageRequest> requestStream, IServerStreamWriter<PingMessageResponse> responseStream, ServerCallContext context)
        {
            var id = string.Empty;
            try
            {
                DateTime responseReceived = DateTime.UtcNow;
                DateTime responseSent = DateTime.UtcNow;

                ServiceState? serviceState = null;

                await foreach (PingMessageRequest message in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
                {
                    id = message.Id;
                    if (serviceState == null)
                    {
                        var serviceStateTaskSource = serviceDictionary.GetOrAdd(id.ToString(), _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));
                        serviceState = await serviceStateTaskSource.Task.WaitWithTimeout(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
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
