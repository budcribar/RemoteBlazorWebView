using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Graph.CallRecords;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteableWebView
{
    public class RemoteWebViewService : RemoteWebView.RemoteWebViewBase
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private readonly ConcurrentDictionary<string, ServiceState> _webViewDictionary;
        private readonly Channel<ClientResponseList> _serviceStateChannel;
        
        public RemoteWebViewService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> rootDictionary, Channel<ClientResponseList> serviceStateChannel)
        {
            _logger = logger;
            _webViewDictionary = rootDictionary;
            _serviceStateChannel = serviceStateChannel;
        }

        public override Task<IdArrayResponse> GetIds(Empty request, ServerCallContext context)
        {
            var results = new IdArrayResponse();
            results.Responses.AddRange(_webViewDictionary.Keys);
            return Task.FromResult(results);
        }
        public override async Task CreateWebView(CreateWebViewRequest request, IServerStreamWriter<WebMessageResponse> responseStream, ServerCallContext context)
        {
            _logger.LogInformation($"CreateWebView Id:{request.Id}");
            if (!_webViewDictionary.ContainsKey(request.Id))
            {
                ServiceState state = new()
                {
                    HtmlHostPath = request.HtmlHostPath,
                    Hostname = request.Hostname,
                    InUse = false,
                    Url = $"https://{context.Host}/app/{request.Id}",
                    Id = request.Id,
                    IPC = new IPC(),
                    Group = request.Group
                };

                // Let home page know client is available
                _webViewDictionary.TryAdd(request.Id, state);

                var list = new ClientResponseList();
                _webViewDictionary?.Values.ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { HostName = x.Hostname, Id = x.Id, State = x.InUse ? ClientState.ShuttingDown : ClientState.Connected, Url = x.Url }));

                await _serviceStateChannel.Writer.WriteAsync(list);
                state.IPC.ResponseStream = responseStream;

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
                await foreach (FileReadRequest message in requestStream.ReadAllAsync())
                {
                    
                    if (message.FileReadCase == FileReadRequest.FileReadOneofCase.Init)
                    {
                        id = message.Init.Id;
                        var task = Task.Run(async () =>
                        {
                            while (true)
                            {
                                var path = await _webViewDictionary[id].FileCollection.Reader.ReadAsync();
                                await responseStream.WriteAsync(new FileReadResponse { Id = id, Path = path });
                                var resetEvent = _webViewDictionary[id].FileDictionary[path].resetEvent;
                                _webViewDictionary[id].FileDictionary[path] = (new MemoryStream(), resetEvent);
                            }
                        });
                    }
                    else
                    {
                        if (message.Data.Data.Length > 0)
                        {
                            var ms = _webViewDictionary[message.Data.Id].FileDictionary[message.Data.Path].stream;
                           
                            if (ms!=null)
                                await ms.WriteAsync(message.Data.Data.Memory);
                        }
                        else _webViewDictionary[message.Data.Id].FileDictionary[message.Data.Path].resetEvent.Set();
                    }
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
            _logger.LogInformation("Shutting down..." + id);

            if (_webViewDictionary.ContainsKey(id))
                _webViewDictionary.Remove(id, out var _);

            var list = new ClientResponseList();
            _webViewDictionary?.Values.ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { HostName = x.Hostname, Id = x.Id, State = x.InUse ? ClientState.ShuttingDown : ClientState.Connected, Url = x.Url }));

            _serviceStateChannel.Writer.WriteAsync(list);
        }

        public override Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            ExShutdown(request.Id);
            return Task.FromResult<Empty>(new Empty());
        }

        public override Task<Empty> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            _webViewDictionary[request.Id].IPC.SendMessage(request.Message);
            return Task.FromResult<Empty>(new Empty());
        }

    }
}
