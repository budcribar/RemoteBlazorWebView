using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Text;
using System.Drawing;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Components;
using RemoteableWebWindowService;
using Microsoft.JSInterop;
using RemoteableWebWindowService.Services;

namespace PeakSwc.RemoteableWebWindows
{
    public class RemoteWebWindowService : RemoteWebWindow.RemoteWebWindowBase
    { 
        private readonly ILogger<RemoteWebWindowService> _logger;
        private readonly ConcurrentDictionary<Guid, ServiceState> _webWindowDictionary;     
        private readonly ConcurrentDictionary<Guid,IPC> _ipc;

        public RemoteWebWindowService(ILogger<RemoteWebWindowService> logger, ConcurrentDictionary<Guid, ServiceState> rootDictionary, ConcurrentDictionary<Guid, IPC> ipc)
        {
            _logger = logger;
            _webWindowDictionary = rootDictionary;
            _ipc = ipc;
        }

        private void Shutdown(Guid id)
        {
            _logger.LogInformation("Shutting down...");
            _webWindowDictionary.Remove(id, out var _);
            _ipc.Remove(id, out var _);
        }

        public override async Task CreateWebWindow(CreateWebWindowRequest request, IServerStreamWriter<WebMessageResponse> responseStream, ServerCallContext context)
        {
            Guid id = Guid.Parse(request.Id);
            if (!_webWindowDictionary.ContainsKey(id))
            {
                ServiceState state = new ServiceState
                {
                    HtmlHostPath = request.HtmlHostPath,
                    Hostname = request.Hostname,
                    Title = request.Title
                };


                if (!_ipc.ContainsKey(id)) _ipc.TryAdd(id, new IPC());
                _ipc[id].ResponseStream = responseStream;
              

                _webWindowDictionary.TryAdd(id, state);

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
                Guid id = new Guid(message.Id);

                if (message.Path == "Initialize")
                {
                    var task2 = Task.Run(async () =>
                    {
                        while (true)
                        {
                            var file = await _webWindowDictionary[id].FileCollection.Reader.ReadAsync();
                            {
                                await responseStream.WriteAsync(new FileReadResponse { Id = id.ToString(), Path = file });
                            }
                        }

                    });

                }
                else
                {

                    _webWindowDictionary[id].FileDictionary[message.Path] = (new MemoryStream(message.Data.ToArray()), _webWindowDictionary[id].FileDictionary[message.Path].mres);
                    _webWindowDictionary[id].FileDictionary[message.Path].mres.Set();
                }
            }
        }
        public override Task<Empty> WaitForExit(IdMessageRequest request, ServerCallContext context)
        {
            Guid id = Guid.Parse(request.Id);

            // TODO
            Thread.Sleep(TimeSpan.FromHours(24));

            Shutdown(id);
            return Task.FromResult<Empty>(new Empty());
        }

        public override Task<Empty> Show(IdMessageRequest request, ServerCallContext context)
        {
            return Task.FromResult<Empty>(new Empty());
        }

        public override Task<Empty> ShowMessage(ShowMessageRequest request, ServerCallContext context)
        {
            //Guid id = Guid.Parse(request.Id);
            //_webWindowDictionary[id].ShowMessage(request.Title, request.Body);
            return Task.FromResult<Empty>(new Empty());
        }

        
        public override Task<Empty> NavigateToUrl(UrlMessageRequest request, ServerCallContext context)
        {
            //Guid id = Guid.Parse(request.Id);

          
            //    _webWindowDictionary[id].NavigateToUrl(request.Url);
            return Task.FromResult<Empty>(new Empty());
        }

        public override Task<Empty> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            Guid id = Guid.Parse(request.Id);

            _ipc[id].SendMessage(request.Message);
            return Task.FromResult<Empty>(new Empty());
        }

        public override Task<Empty> NavigateToLocalFile(FileMessageRequest request, ServerCallContext context)
        {
            //Guid id = Guid.Parse(request.Id);
            //_webWindowDictionary[id].NavigateToLocalFile(request.Path);
            return Task.FromResult<Empty>(new Empty());
        }
       
        public override Task<Empty> NavigateToString(StringRequest request, ServerCallContext context)
        {
            //Guid id = Guid.Parse(request.Id);
            // TODO _webWindowDictionary[id].NavigateToString(request.Request);
            return Task.FromResult<Empty>(new Empty());
        }

       
    }
}
