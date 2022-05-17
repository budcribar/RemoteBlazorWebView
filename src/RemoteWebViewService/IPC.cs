using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class IPC
    {
        private readonly Channel<WebMessageResponse> responseChannel = Channel.CreateUnbounded<WebMessageResponse>();
        private readonly Channel<StringRequest> browserResponseChannel = Channel.CreateUnbounded<StringRequest>();
        private readonly List<IServerStreamWriter<StringRequest>> browserResponseStreamList = new();
        private readonly List<StringRequest> messageHistory = new ();

        public IServerStreamWriter<WebMessageResponse>? ClientResponseStream { get; set; }
        public bool BrowserResponseStream (IServerStreamWriter<StringRequest> serverStreamWriter) {
            bool isPrimary = true;
            lock (browserResponseStreamList)
            {
                isPrimary = messageHistory.Count == 0;
                browserResponseStreamList.Add(serverStreamWriter);
                if (browserResponseStreamList.Count > 1)
                {
                    messageHistory.ForEach(async m => await serverStreamWriter.WriteAsync(m));

                }
            }
            return isPrimary;
            
        }

        public Task ClientTask { get; }
        public Task BrowserTask { get; }

        public ValueTask SendMessage(string eventName, params object[] args)
        {
            var message = $"{eventName}:{JsonSerializer.Serialize(args)}";
            return browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message });
        }

        public ValueTask SendMessage(string message)
        {
            return browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message });
        }

        public IPC(CancellationToken token, ILogger<RemoteWebViewService> logger, bool enableMirrors)
        {
            ClientTask = Task.Factory.StartNew(async () =>
            {
                await foreach (var m in responseChannel.Reader.ReadAllAsync(token))
                {
                    // Serialize the write
                    await (ClientResponseStream?.WriteAsync(m) ?? Task.CompletedTask);
                    logger.LogInformation($"Browser -> WebView {m.Response}");
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            BrowserTask = Task.Factory.StartNew(async () =>
            {
                await foreach (var m in browserResponseChannel.Reader.ReadAllAsync(token))
                {
                    lock (browserResponseStreamList)
                    {
                        if(!m.Request.Contains("EndInvokeDotNet") && enableMirrors)
                            messageHistory.Add(m);
                        // Serialize the write
                        int i = 0;
                        foreach (var stream in browserResponseStreamList)
                        {
                            // Skip EndInvokeDotNet on the mirrors
                            if (i==0 || !m.Request.Contains("EndInvokeDotNet"))
                            {
                                stream.WriteAsync(m);
                                logger.LogInformation($"WebView -> Browser {m.Id} {m.Request}");
                            }
                               
                            i++;
                        }
                    }
                   
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        }

        public ValueTask ReceiveMessage(WebMessageResponse message)
        {
            return responseChannel.Writer.WriteAsync(message);
        }

        public Task LocationChanged(Point point)
        {
            return (ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "location:" + JsonSerializer.Serialize(point) }) ?? Task.CompletedTask);
        }
        public Task SizeChanged(Size size)
        {
            return (ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "size:" + JsonSerializer.Serialize(size) }) ?? Task.CompletedTask);
        }
    }
}
