using Grpc.Core;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class IPC
    {
        private readonly CancellationTokenSource cts = new();
        private readonly Channel<WebMessageResponse> responseChannel = Channel.CreateUnbounded<WebMessageResponse>();
        private readonly Channel<StringRequest> browserResponseChannel = Channel.CreateUnbounded<StringRequest>();

        public IServerStreamWriter<WebMessageResponse>? ClientResponseStream { get; set; }
        public IServerStreamWriter<StringRequest>? BrowserResponseStream { get; set; }

        public Task ClientTask { get; }
        public Task BrowserTask { get; }

        public async void SendMessage(string eventName, params object[] args)
        {
            var message = $"{eventName}:{JsonSerializer.Serialize(args)}";
            await browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message });
        }

        public async void SendMessage(string message)
        {
            await browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message });
        }


        public IPC()
        {
            ClientTask = Task.Factory.StartNew(async () =>
            {
                await foreach (var m in responseChannel.Reader.ReadAllAsync(cts.Token))
                {
                    // Serialize the write
                    await (ClientResponseStream?.WriteAsync(m) ?? Task.CompletedTask);
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            BrowserTask = Task.Factory.StartNew(async () =>
            {
                await foreach (var m in browserResponseChannel.Reader.ReadAllAsync(cts.Token))
                {
                    // Serialize the write
                    await (BrowserResponseStream?.WriteAsync(m) ?? Task.CompletedTask);
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        }

        public async void ReceiveMessage(WebMessageResponse message)
        {
            await responseChannel.Writer.WriteAsync(message);
        }

        public void Shutdown()
        {
            cts.Cancel();
        }

        public async void LocationChanged(Point point)
        {
            await (ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "location:" + JsonSerializer.Serialize(point) }) ?? Task.CompletedTask);
        }
        public async void SizeChanged(Size size)
        {
            await (ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "size:" + JsonSerializer.Serialize(size) }) ?? Task.CompletedTask);
        }
    }
}
