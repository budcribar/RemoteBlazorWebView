using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteableWebView
{
    public class RemoteWebViewService : RemoteWebWindow.RemoteWebWindowBase
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private readonly ConcurrentDictionary<string, ServiceState> _webWindowDictionary;
        private readonly ConcurrentDictionary<string, byte[]> _fileCache = new();
        private readonly Channel<ClientResponseList> _serviceStateChannel;
        private readonly bool useCache = false;

        public RemoteWebViewService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, ServiceState> rootDictionary, Channel<ClientResponseList> serviceStateChannel)
        {
            _logger = logger;
            _webWindowDictionary = rootDictionary;
            _serviceStateChannel = serviceStateChannel;
        }

        public override Task<IdArrayResponse> GetIds(Empty request, ServerCallContext context)
        {
            var results = new IdArrayResponse();
            results.Responses.AddRange(_webWindowDictionary.Keys);
            return Task.FromResult(results);
        }
        public override async Task CreateWebWindow(CreateWebWindowRequest request, IServerStreamWriter<WebMessageResponse> responseStream, ServerCallContext context)
        {
            if (!_webWindowDictionary.ContainsKey(request.Id))
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
                _webWindowDictionary.TryAdd(request.Id, state);

                var list = new ClientResponseList();
                _webWindowDictionary?.Values.ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { HostName = x.Hostname, Id = x.Id, State = x.InUse ? ClientState.ShuttingDown : ClientState.Connected, Url = x.Url }));

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
                await foreach (var message in requestStream.ReadAllAsync())
                {
                    if (message.Path == "Initialize")
                    {
                        id = message.Id;
                        var task = Task.Run(async () =>
                        {
                            while (true)
                            {
                                var file = await _webWindowDictionary[message.Id].FileCollection.Reader.ReadAsync();
                                {
                                    if (_fileCache.ContainsKey(file) && useCache)
                                    {
                                        // TODO need to further identify file by hash
                                        var resetEvent = _webWindowDictionary[message.Id].FileDictionary[file].resetEvent;
                                        _webWindowDictionary[message.Id].FileDictionary[file] = (new MemoryStream(_fileCache[file]), resetEvent);
                                        resetEvent.Set();
                                    }
                                    else
                                    {
                                        await responseStream.WriteAsync(new FileReadResponse { Id = message.Id, Path = file });
                                    }

                                }
                            }

                        });

                    }
                    else
                    {
                        var bytes = message.Data.ToArray();
                        var resetEvent = _webWindowDictionary[message.Id].FileDictionary[message.Path].resetEvent;
                        _webWindowDictionary[message.Id].FileDictionary[message.Path] = (new MemoryStream(bytes), resetEvent);
                        resetEvent.Set();

                        // TODO Further identify file by hash
                        if (bytes.Length > 0)
                            _fileCache.TryAdd(message.Path, bytes);
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

            if (_webWindowDictionary.ContainsKey(id))
                _webWindowDictionary.Remove(id, out var _);

            var list = new ClientResponseList();
            _webWindowDictionary?.Values.ToList().ForEach(x => list.ClientResponses.Add(new ClientResponse { HostName = x.Hostname, Id = x.Id, State = x.InUse ? ClientState.ShuttingDown : ClientState.Connected, Url = x.Url }));

            _serviceStateChannel.Writer.WriteAsync(list);
        }

        public override Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            ExShutdown(request.Id);
            return Task.FromResult<Empty>(new Empty());
        }

        public override Task<Empty> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            _webWindowDictionary[request.Id].IPC.SendMessage(request.Message);
            return Task.FromResult<Empty>(new Empty());
        }

    }
}
