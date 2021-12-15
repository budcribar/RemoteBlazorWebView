using Azure.Core;
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
    public class RemoteWebViewService : WebViewIPC.WebViewIPCBase
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private ConcurrentDictionary<string, ServiceState> ServiceDictionary { get; init; }
        private readonly ConcurrentDictionary<string, Channel<string>> _serviceStateChannel;
        private readonly ConcurrentBag<ServiceState> _serviceStates;
        ShutdownService ShutdownService { get; }

        public RemoteWebViewService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> serviceDictionary, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentBag<ServiceState> serviceStates, ShutdownService shutdownService)
        {
            _logger = logger;
            ServiceDictionary = serviceDictionary;
            _serviceStateChannel = serviceStateChannel;
            _serviceStates = serviceStates;
            ShutdownService = shutdownService;
        }

        public override Task<IdArrayResponse> GetIds(Empty request, ServerCallContext context)
        {
            var results = new IdArrayResponse();
            results.Responses.AddRange(ServiceDictionary.Keys);
            return Task.FromResult(results);
        }

        public override async Task CreateWebView(CreateWebViewRequest request, IServerStreamWriter<WebMessageResponse> responseStream, ServerCallContext context)
        {
            _logger.LogInformation($"CreateWebView Id:{request.Id}");

            if (!ServiceDictionary.ContainsKey(request.Id))
            {
                ServiceState state = new()
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
                _serviceStates.Add(state);
                // Let home page know client is available
                ServiceDictionary.TryAdd(request.Id, state);

                var list = new ClientResponseList();
                ServiceDictionary?.Values.ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { Markup = x.Markup, Id = x.Id, State = x.InUse ? ClientState.Connected : ClientState.ShuttingDown, Url = x.Url }));
               
                _serviceStateChannel.Values.ToList().ForEach(x => x.Writer.TryWrite($"Start:{request.Id}"));
                state.IPC.ClientResponseStream = responseStream;

                await responseStream.WriteAsync(new WebMessageResponse { Response = "created:" });

                using (CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, state.CancellationTokenSource.Token))
                {
                    while (!linkedToken.IsCancellationRequested)
                    {
                        await Task.Delay(1000, linkedToken.Token).ConfigureAwait(false);
                    }
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
                    if (message.FileReadCase == FileReadRequest.FileReadOneofCase.Init)
                        id = message.Init.Id;
                    else
                        id = message.Data.Id;

                    if (ServiceDictionary.TryGetValue(id, out var serviceState))
                    {
                        if (serviceState.CancellationTokenSource.IsCancellationRequested)
                            break;

                        if (message.FileReadCase == FileReadRequest.FileReadOneofCase.Init)
                        {
                            serviceState.FileReaderTask = Task.Factory.StartNew(async () =>
                            {
                                while (!serviceState.CancellationTokenSource.IsCancellationRequested)
                                {
                                    var path = await serviceState.FileCollection.Reader.ReadAsync(serviceState.CancellationTokenSource.Token);
                                    await responseStream.WriteAsync(new FileReadResponse { Id = id, Path = path });
                                }
                            }, serviceState.CancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                        }
                        else
                        {
                            if (message.Data.Data.Length > 0)
                            {
                                var ms = serviceState.FileDictionary[message.Data.Path].stream;
                                if (ms != null)
                                    await ms.WriteAsync(message.Data.Data.Memory);
                            }
                            else
                            {
                                // Trigger the stream read
                                serviceState.FileDictionary[message.Data.Path].resetEvent.Set();
                            }
                        }
                    }

                    else break;
                }
            }
            catch (Exception ex)
            {
                ShutdownService.Shutdown(id,ex);
            }
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
                    if (!ServiceDictionary.TryGetValue(id, out ServiceState? serviceState))
                    {
                        await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true });
                        ShutdownService.Shutdown(id);
                        break; ;
                    }

                    if (message.Initialize)
                    {
                        serviceState.PingTask = Task.Factory.StartNew(async () =>
                        {
                            while (!serviceState.CancellationTokenSource.IsCancellationRequested)
                            {
                                responseSent = DateTime.Now;
                                await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = false });
                                await Task.Delay(TimeSpan.FromSeconds(message.PingIntervalSeconds), serviceState.CancellationTokenSource.Token).ConfigureAwait(false);
                                if (responseReceived < responseSent)
                                {
                                    await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true });
                                    ShutdownService.Shutdown(id);
                                    break;
                                }

                            }
                        }, serviceState.CancellationTokenSource.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
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
                ShutdownService.Shutdown(id, ex);
            }
        }

        public override Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            ShutdownService.Shutdown(request.Id);
            return Task.FromResult(new Empty());
        }

        public override Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            if (ServiceDictionary.TryGetValue(request.Id,out ServiceState? serviceState))          
			{
                serviceState.IPC.SendMessage(request.Message);
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = true });
            }

            return Task.FromResult( new SendMessageResponse { Id = request.Id, Success = false });
        }

    }
}
