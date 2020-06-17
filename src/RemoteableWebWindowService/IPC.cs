using Grpc.Core;
using Microsoft.AspNetCore.SignalR;
//using Newtonsoft.Json;
using PeakSwc.RemoteableWebWindows;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading;

namespace RemoteableWebWindowService
{
    public class IPC
    {
        public string Name { get; set; }
        public IServerStreamWriter<WebMessageResponse> ResponseStream { get; set; }
        public IServerStreamWriter<StringRequest> BrowserResponseStream { get; set; }

        private readonly CancellationTokenSource cts = new CancellationTokenSource();

        private readonly Channel<WebMessageResponse> responseChannel = Channel.CreateUnbounded<WebMessageResponse>();
        private readonly Channel<StringRequest> browserResponseChannel = Channel.CreateUnbounded<StringRequest>();

        public async void SendMessage(string eventName, params object[] args)
        {
            var message = $"{eventName}:{JsonSerializer.Serialize(args)}";
            await browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message });
        }

        public async void SendMessage(string message)
        {
            await browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message });
        }


        public IPC (){
            Task.Run(async () =>
            {
                await foreach (var m in responseChannel.Reader.ReadAllAsync())
                {
                    // Serialize the write
                    await ResponseStream.WriteAsync(m);
                }
            }, cts.Token);

            Task.Run(async () =>
            {
                await foreach (var m in browserResponseChannel.Reader.ReadAllAsync())
                {
                    // Serialize the write
                    await BrowserResponseStream.WriteAsync(m);
                }
            }, cts.Token);

        }

        public async void ReceiveMessage(string message)
        {
            await responseChannel.Writer.WriteAsync(new WebMessageResponse { Response = "webmessage:" + message });
        }

        public void Shutdown()
        {
            cts.Cancel();
        }

        public async void LocationChanged(Point point)
        {
            await ResponseStream.WriteAsync(new WebMessageResponse { Response = "location:" + JsonSerializer.Serialize(point) });
        }
        public async void SizeChanged(Size size)
        {
            await ResponseStream.WriteAsync(new WebMessageResponse { Response = "size:" + JsonSerializer.Serialize(size) });
        }
    }
}
