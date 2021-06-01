using Grpc.Core;
//using Newtonsoft.Json;
using PeakSwc.RemoteableWebWindows;
using System.Drawing;
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading;

namespace RemoteableWebWindowService
{
    public class IPC
    {
        private readonly CancellationTokenSource cts = new();
        private readonly Channel<WebMessageResponse> responseChannel = Channel.CreateUnbounded<WebMessageResponse>();
        private readonly Channel<StringRequest> browserResponseChannel = Channel.CreateUnbounded<StringRequest>();

        public IServerStreamWriter<WebMessageResponse>? ResponseStream { get; set; }
        public IServerStreamWriter<StringRequest>? BrowserResponseStream { get; set; }

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
                    await (ResponseStream?.WriteAsync(m) ?? Task.CompletedTask);
                }
            }, cts.Token);

            Task.Run(async () =>
            {
                await foreach (var m in browserResponseChannel.Reader.ReadAllAsync())
                {
                    // Serialize the write
                    await (BrowserResponseStream?.WriteAsync(m) ?? Task.CompletedTask);
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
            await (ResponseStream?.WriteAsync(new WebMessageResponse { Response = "location:" + JsonSerializer.Serialize(point) }) ?? Task.CompletedTask);
        }
        public async void SizeChanged(Size size)
        {
            await (ResponseStream?.WriteAsync(new WebMessageResponse { Response = "size:" + JsonSerializer.Serialize(size) }) ?? Task.CompletedTask);
        }
    }
}
