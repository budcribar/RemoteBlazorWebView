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
    public class RemoteWebViewService : WebViewIPC.WebViewIPCBase
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private readonly ConcurrentDictionary<string, ServiceState> _serviceDictionary;
        private readonly ConcurrentDictionary<string, Channel<string>> _serviceStateChannel;
        private readonly ShutdownService _shutdownService;

        public RemoteWebViewService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> serviceDictionary, ConcurrentDictionary<string, Channel<string>> serviceStateChannel,ShutdownService shutdownService)
        {
            _logger = logger;
            _serviceDictionary = serviceDictionary;
            _serviceStateChannel = serviceStateChannel;
            _shutdownService = shutdownService;
        }

        public override Task<IdArrayResponse> GetIds(Empty request, ServerCallContext context)
        {
            var results = new IdArrayResponse();
            results.Responses.AddRange(_serviceDictionary.Keys);
            return Task.FromResult(results);
        }

        public override async Task CreateWebView(CreateWebViewRequest request, IServerStreamWriter<WebMessageResponse> responseStream, ServerCallContext context)
        {
            _logger.LogInformation($"CreateWebView Id:{request.Id}");

            ServiceState state = new(_logger,request.EnableMirrors)
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

            if (_serviceDictionary.TryAdd(request.Id, state))
            { 
                _serviceStateChannel.Values.ToList().ForEach(x => x.Writer.TryWrite($"Start:{request.Id}"));
                state.IPC.ClientResponseStream = responseStream;

                await responseStream.WriteAsync(new WebMessageResponse { Response = "created:" });

                using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, state.Token);
                try
                {
                    while (!linkedToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, linkedToken.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _shutdownService.Shutdown(request.Id, ex);
                }
            }
            else
            {
                await responseStream.WriteAsync(new WebMessageResponse { Response = "createFailed:" });
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

                    if (_serviceDictionary.TryGetValue(id, out var serviceState))
                    {
                        if (serviceState.Token.IsCancellationRequested)
                            break;

                        if (message.FileReadCase == FileReadRequest.FileReadOneofCase.Init)
                        {
                            serviceState.FileReaderTask = Task.Factory.StartNew(async () =>
                            {
                                while (!serviceState.Token.IsCancellationRequested)
                                {
                                    var path = await serviceState.FileCollection.Reader.ReadAsync(serviceState.Token);
                                    await responseStream.WriteAsync(new FileReadResponse { Id = id, Path = path });
                                }
                            }, serviceState.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                        }
                        else if (message.FileReadCase == FileReadRequest.FileReadOneofCase.Length)
                        {
                            var fileEntry = serviceState.FileDictionary[message.Length.Path];
                            fileEntry.Length = message.Length.Length;
                            //fileEntry.ResetEvent.Set();
                        }
                        else if(message.FileReadCase == FileReadRequest.FileReadOneofCase.Data)
                        {
                            if (message.Data.Data.Length > 0)
                            {
                                var fileEntry = serviceState.FileDictionary[message.Data.Path];
                                // TODO is there a limit on the Pipe write?
                                fileEntry.Pipe.Writer.Write(message.Data.Data.Span);
                            }
                            else
                            {
                                // Trigger the stream read
                                var fileEntry = serviceState.FileDictionary[message.Data.Path];
                                fileEntry.Pipe.Writer.Complete();
                                fileEntry.ResetEvent.Set();
                            }
                        }
                    }

                    else break;
                }
            }
            catch (Exception ex)
            {
                _shutdownService.Shutdown(id,ex);
            }
        }

        public override Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            _shutdownService.Shutdown(request.Id);
            return Task.FromResult(new Empty());
        }

        public override Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            if (_serviceDictionary.TryGetValue(request.Id,out ServiceState? serviceState))          
			{
                serviceState.IPC.SendMessage(request.Message).AsTask().Wait();
              
                if (request.Message.Contains("BeginInvokeJS") && request.Message.Contains("import"))
                {
                    serviceState.ImportResetEvent.Reset();
                    serviceState.ImportId = request.Message.Split(",")[1];
                   
                }

                if (request.Message.Contains("BeginInvokeJS") && !string.IsNullOrEmpty(serviceState.ImportId))
                {
                    // Need to wait for previous BeginInvokeJS to finish
                    // serviceState.ImportResetEvent.Wait(context.CancellationToken);
                    serviceState.ImportId = string.Empty;
                }


                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = true });
            }

            return Task.FromResult( new SendMessageResponse { Id = request.Id, Success = false });
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
                    if (!_serviceDictionary.TryGetValue(id, out ServiceState? serviceState))
                    {
                        await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true });
                        _shutdownService.Shutdown(id);
                        break; ;
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
                                    _shutdownService.Shutdown(id);
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
                _shutdownService.Shutdown(id, ex);
            }
        }
    }
}
