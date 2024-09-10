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
                serviceStateChannel.Values.ToList().ForEach(x => x.Writer.TryWrite($"Start:{request.Id}"));
                state.IPC.ClientResponseStream = responseStream;

                await responseStream.WriteAsync(new WebMessageResponse { Response = "created:" });


            }
            else
            {
                logger.LogError($"CreateWebView Id:{request.Id} failed to add client");
                await responseStream.WriteAsync(new WebMessageResponse { Response = "createFailed:" });
                return;
            }
            

            using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, state.Token);
            try
            {
                while (!linkedToken.IsCancellationRequested)
                {
                    await Task.Delay(30).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"CreateWebView Id:{request.Id} failed {ex.Message}");
                shutdownService.Shutdown(request.Id, ex);
            }
         
        }

        public override async Task FileReader(IAsyncStreamReader<FileReadRequest> requestStream, IServerStreamWriter<FileReadResponse> responseStream, ServerCallContext context)
        {
            var id = string.Empty;
            try
            {
                await foreach (FileReadRequest message in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    id = message.Id;

                    if (serviceDictionary.TryGetValue(id, out var serviceState))
                    {
                        if (serviceState.Token.IsCancellationRequested)
                            break;

                        if (message.FileReadCase == FileReadRequest.FileReadOneofCase.Init)
                        {
                            serviceState.FileReaderTask = Task.Factory.StartNew(async () =>
                            {
                                while (!serviceState.Token.IsCancellationRequested)
                                {
                                    var fileEntry = await serviceState.FileCollection.Reader.ReadAsync(serviceState.Token);
                                    await responseStream.WriteAsync(new FileReadResponse { Id = id, Path = fileEntry.Path, Instance=fileEntry.Instance });
                                }
                            }, serviceState.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                        }
                        else if (message.FileReadCase == FileReadRequest.FileReadOneofCase.Length)
                        {
                            var fileEntry = serviceState.FileDictionary[message.Length.Path][message.Length.Instance];
                            fileEntry.Length = message.Length.Length;
                            fileEntry.Semaphore.Release();
                        }
                        else if(message.FileReadCase == FileReadRequest.FileReadOneofCase.Data)
                        {
                            var fileEntry = serviceState.FileDictionary[message.Data.Path][message.Data.Instance]; 
                            if (message.Data.Data.Length > 0)
                            {        
                                // TODO is there a limit on the Pipe write?
                                fileEntry.Pipe.Writer.Write(message.Data.Data.Span);
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
            catch (Exception ex)
            {
                shutdownService.Shutdown(id,ex);
            }
        }

        public override Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            shutdownService.Shutdown(request.Id);
            return Task.FromResult(new Empty());
        }

        public override async Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            if (serviceDictionary.TryGetValue(request.Id,out ServiceState? serviceState))          
			{
                await serviceState.IPC.SendMessage(request.Message);

                return new SendMessageResponse { Id = request.Id, Success = true };
            }

            return new SendMessageResponse { Id = request.Id, Success = false };
        }

        public override async Task Ping(IAsyncStreamReader<PingMessageRequest> requestStream, IServerStreamWriter<PingMessageResponse> responseStream, ServerCallContext context)
        {
            var id = string.Empty;
            try
            {
                DateTime responseReceived = DateTime.Now;
                DateTime responseSent = DateTime.Now;

                await foreach (PingMessageRequest message in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    id = message.Id;
                    if (!serviceDictionary.TryGetValue(id, out ServiceState? serviceState))
                    {
                        await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true });
                        shutdownService.Shutdown(id);
                        break;
                    }

                    if (message.Initialize)
                    {
                        serviceState.PingTask = Task.Factory.StartNew(async () =>
                        {
                            while (!serviceState.Token.IsCancellationRequested)
                            {
                                responseSent = DateTime.Now;
                                await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = false });
                                await Task.Delay(TimeSpan.FromSeconds(message.PingIntervalSeconds), serviceState.Token).ConfigureAwait(false);
                                if (responseReceived < responseSent)
                                {
                                    await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true });
                                    shutdownService.Shutdown(id);
                                    break;
                                }

                            }
                        }, serviceState.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    }
                    else
                    {
                        responseReceived = DateTime.Now;
                        var delta = responseReceived.Subtract(responseSent);
                        if (delta > serviceState.MaxClientPing)
                            serviceState.MaxClientPing = delta;
                    }
                }
            }
            catch (Exception ex)
            {
                shutdownService.Shutdown(id, ex);
            }
        }
    }
}
