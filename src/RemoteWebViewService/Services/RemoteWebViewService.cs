using Azure.Core;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
      
        public RemoteWebViewService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> serviceDictionary, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentBag<ServiceState> serviceStates)
        {
            _logger = logger;
            ServiceDictionary = serviceDictionary;
            _serviceStateChannel = serviceStateChannel;
            _serviceStates = serviceStates;
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

                while (!context.CancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000);
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
                    {
                        id = message.Init.Id;

                        ServiceDictionary[id].FileReaderTask = Task.Run(async () =>
                        {
                            while (true)
                            {
                                if (!ServiceDictionary.ContainsKey(id)) break;
                                var path = await ServiceDictionary[id].FileCollection.Reader.ReadAsync(context.CancellationToken);
                                await responseStream.WriteAsync(new FileReadResponse { Id = id, Path = path });
                            }
                        }, context.CancellationToken);
                    }
                    else
                    {
                        if (message.Data.Data.Length > 0)
                        {
                            var ms = ServiceDictionary[message.Data.Id].FileDictionary[message.Data.Path].stream;
                            if (ms != null)
                                await ms.WriteAsync(message.Data.Data.Memory);
                        }
                        else
                        {
                            // Trigger the stream read
                            ServiceDictionary[message.Data.Id].FileDictionary[message.Data.Path].resetEvent.Set();
                        }

                    }
                }
            }
            catch (Exception)
            {
                ExShutdown(id);

                // Client has shut down
            }

        }

        public override async Task Ping(IAsyncStreamReader<PingMessageRequest> requestStream, IServerStreamWriter<PingMessageResponse> responseStream, ServerCallContext context)
        {
            var id = string.Empty;
            try
            {
                var responseReceived = false;
                await foreach (PingMessageRequest message in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    if (message.Initialize)
                    {
                        id = message.Id;

                        ServiceDictionary[id].PingTask = Task.Run(async () =>
                        {
                            while (true)
                            {
                                responseReceived = false;
                                await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = false }); 
                                await Task.Delay(TimeSpan.FromSeconds(message.PingIntervalSeconds));
                                if (!responseReceived)
                                {
                                    await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true });
                                    ExShutdown(id);
                                    break;
                                }
                                    
                            }
                        }, context.CancellationToken);

                    }
                    else responseReceived = true;
                }
            }
            catch (Exception)
            {
                ExShutdown(id);

                // Client has shut down
            }
        }

            private void ExShutdown(string id)
        {
            _logger.LogWarning("Shutting down..." + id);

            if (ServiceDictionary.ContainsKey(id))
            {
                ServiceDictionary.Remove(id, out var client);
                if (client != null)
                {
                    client.IPC.Shutdown();
                    client.InUse = false;
                }             
            }
            _serviceStateChannel.Values.ToList().ForEach(x => x.Writer.TryWrite($"Shutdown:{id}"));
        }

        public override Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            ExShutdown(request.Id);
            return Task.FromResult<Empty>(new Empty());
        }

       
        public override Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            if (ServiceDictionary.ContainsKey(request.Id))
			{
                ServiceDictionary[request.Id].IPC.SendMessage(request.Message);
                return Task.FromResult(new SendMessageResponse { Id = request.Id, Success = true });
            }

            return Task.FromResult( new SendMessageResponse { Id = request.Id, Success = false });
        }

    }
}
