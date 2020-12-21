using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using RemoteableWebWindowService;
using RemoteableWebWindowService.Services;

namespace PeakSwc.RemoteableWebWindows
{
    public class RemoteWebWindowService : RemoteWebWindow.RemoteWebWindowBase
    { 
        private readonly ILogger<RemoteWebWindowService> _logger;
        private readonly ConcurrentDictionary<string, ServiceState> _webWindowDictionary;     
        private readonly ConcurrentDictionary<string, IPC> _ipc;
        private readonly ConcurrentDictionary<string, byte[]> _fileCache = new ConcurrentDictionary<string, byte[]>();
        private readonly bool useCache = false;

        public RemoteWebWindowService(ILogger<RemoteWebWindowService> logger, ConcurrentDictionary<string, ServiceState> rootDictionary, ConcurrentDictionary<string, IPC> ipc)
        {
            _logger = logger;
            _webWindowDictionary = rootDictionary;
            _ipc = ipc;
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
                ServiceState state = new ServiceState
                {
                    HtmlHostPath = request.HtmlHostPath,
                    Hostname = request.Hostname,
                };

                if (!_ipc.ContainsKey(request.Id)) _ipc.TryAdd(request.Id, new IPC());
                _ipc[request.Id].ResponseStream = responseStream;
              
                _webWindowDictionary.TryAdd(request.Id, state);

                await responseStream.WriteAsync(new WebMessageResponse { Response = "created:" });

                while (!context.CancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(1000);
                }
            }
            
        }

        public override async Task FileReader(IAsyncStreamReader<FileReadRequest> requestStream, IServerStreamWriter<FileReadResponse> responseStream, ServerCallContext context)
        {
            var id = "";
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
                        _fileCache.TryAdd(message.Path,bytes);
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
            _logger.LogInformation("Shutting down..."  + id);

            if (_webWindowDictionary.ContainsKey(id))
                _webWindowDictionary.Remove(id, out var _);

            if (_ipc.ContainsKey(id))
                _ipc.Remove(id, out var _);
        }

        public override Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            ExShutdown(request.Id);
            return Task.FromResult<Empty>(new Empty());
        }

        public override Task<Empty> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            _ipc[request.Id].SendMessage(request.Message);
            return Task.FromResult<Empty>(new Empty());
        }

    }
}
