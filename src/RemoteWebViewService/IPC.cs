using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
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
        private readonly List<StringRequest> messageHistory = new ();
        private readonly ConcurrentDictionary<IServerStreamWriter<StringRequest>, BlockingCollection<StringRequest>> observers = new();
        private IServerStreamWriter<StringRequest>? primaryStream;
        private readonly ILogger<RemoteWebViewService> logger;

        public IServerStreamWriter<WebMessageResponse>? ClientResponseStream { get; set; }
        public bool BrowserResponseStream (IServerStreamWriter<StringRequest> serverStreamWriter, CancellationTokenSource linkedToken) {
         
            lock (messageHistory)
            {
                if (messageHistory.Count == 0)
                    primaryStream = serverStreamWriter;

                var messages = new BlockingCollection<StringRequest>();
                foreach (var message in messageHistory)
                {
                    messages.Add(message);
                }

                observers.TryAdd(serverStreamWriter, messages);
            }

            // TODO Keep track of tasks
            Task.Run(() => ProcessMessages(serverStreamWriter,linkedToken.Token));

            return primaryStream != null;        
        }

        public Task ClientTask { get; }
        public Task BrowserTask { get; }

       
        private async Task WriteMessage(IServerStreamWriter<StringRequest> serverStreamWriter, StringRequest message, bool isMirror)
        {
            await serverStreamWriter.WriteAsync(message);
            if (message.Request.Contains("BeginInvokeJS") && message.Request.Contains("import"))
            {
                if (isMirror)
                    await Task.Delay(1000);
            }
        }

        public async ValueTask SendMessage(string message)
        {
            await browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message });
        }

        public IPC(CancellationToken token, ILogger<RemoteWebViewService> logger, bool enableMirrors)
        {
            this.logger = logger;
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
                    lock (messageHistory)
                    {
                        if (!m.Request.Contains("EndInvokeDotNet") && enableMirrors)
                            messageHistory.Add(m);
                        foreach (var observer in observers.Keys)
                        {
                            observers[observer].Add(m);
                        }
                    }

                   
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        }

        public async Task ProcessMessages(IServerStreamWriter<StringRequest> serverStreamWriter, CancellationToken cancellationToken)
        {
            bool isPrimary = serverStreamWriter == primaryStream;

            if (observers.TryGetValue(serverStreamWriter, out var updates))
            {
                foreach (var request in updates.GetConsumingEnumerable(cancellationToken))
                {
                    if (isPrimary || !request.Request.Contains("EndInvokeDotNet"))
                    {
                        await WriteMessage(serverStreamWriter, request, !isPrimary);
                        logger.LogInformation($"WebView -> Browser {request.Id} {request.Request}");
                    }
                       
                }
            }
        }

        public ValueTask ReceiveMessage(WebMessageResponse message)
        {
            return responseChannel.Writer.WriteAsync(message);
        }

        public Task LocationChanged(Point point)
        {
            return (ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "location:" + JsonSerializer.Serialize(point, JsonContext.Default.Point) }) ?? Task.CompletedTask);
        }
        public Task SizeChanged(Size size)
        {
            return (ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "size:" + JsonSerializer.Serialize(size, JsonContext.Default.Size) }) ?? Task.CompletedTask);
        }
    }
}
