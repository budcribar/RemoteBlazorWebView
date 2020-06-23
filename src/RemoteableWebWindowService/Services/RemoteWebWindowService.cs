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

        public RemoteWebWindowService(ILogger<RemoteWebWindowService> logger, ConcurrentDictionary<string, ServiceState> rootDictionary, ConcurrentDictionary<string, IPC> ipc)
        {
            _logger = logger;
            _webWindowDictionary = rootDictionary;
            _ipc = ipc;
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
            await foreach (var message in requestStream.ReadAllAsync())
            {    
                if (message.Path == "Initialize")
                {
                    var task2 = Task.Run(async () =>
                    {
                        while (true)
                        {
                            var file = await _webWindowDictionary[message.Id].FileCollection.Reader.ReadAsync();
                            {
                                await responseStream.WriteAsync(new FileReadResponse { Id = message.Id, Path = file });
                            }
                        }

                    });

                }
                else
                {
                    var resetEvent = _webWindowDictionary[message.Id].FileDictionary[message.Path].resetEvent;
                    _webWindowDictionary[message.Id].FileDictionary[message.Path] = (new MemoryStream(message.Data.ToArray()), resetEvent);
                    resetEvent.Set();
                }
            }
        }

        public override Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            _logger.LogInformation("Shutting down...");
            _webWindowDictionary.Remove(request.Id, out var _);
            _ipc.Remove(request.Id, out var _);
            return Task.FromResult<Empty>(new Empty());
        }

        public override Task<Empty> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            _ipc[request.Id].SendMessage(request.Message);
            return Task.FromResult<Empty>(new Empty());
        }

    }
}
