using Grpc.Core;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class IPC : IDisposable
    {
        private readonly Channel<WebMessageResponse> responseChannel = Channel.CreateUnbounded<WebMessageResponse>();
        private readonly Channel<StringRequest> browserResponseChannel = Channel.CreateUnbounded<StringRequest>();
        private readonly List<StringRequest> messageHistory = [];
        private readonly ConcurrentDictionary<BrowserResponseNode, BlockingCollection<StringRequest>> observers = new();
       
        private readonly ILogger<RemoteWebViewService> logger;

        public IServerStreamWriter<WebMessageResponse>? ClientResponseStream { get; set; }
        public void BrowserResponseStream ( BrowserResponseNode brn, CancellationTokenSource linkedToken) {
         
            lock (messageHistory)
            {
                var messages = new BlockingCollection<StringRequest>();
                foreach (var message in messageHistory)
                {
                    messages.Add(message);
                }

                observers.TryAdd(brn, messages);
            }

            ProcessMessagesTask = Task.Run(() => ProcessMessages(brn,linkedToken.Token));  
        }

        public Task ClientTask { get; }
        public Task BrowserTask { get; }

        private Task ProcessMessagesTask { get; set; }
       
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
                try
                {
                    await foreach (var m in responseChannel.Reader.ReadAllAsync(token))
                    {
                        // Serialize the write
                        await (ClientResponseStream?.WriteAsync(m) ?? Task.CompletedTask);
                        logger.LogInformation($"Browser -> WebView {m.Response}");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError($"Client Task has shutdown {ex.Message}");
                }
               
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            BrowserTask = Task.Factory.StartNew(async () =>
            {
                try
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
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError($"Browser Task has shutdown {ex.Message}");
                }      
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        }

        private async Task ProcessMessages(BrowserResponseNode brn, CancellationToken cancellationToken)
        {
          
            if (observers.TryGetValue(brn, out var updates))
            {
                foreach (var request in updates.GetConsumingEnumerable(cancellationToken))
                {
                    if (brn.IsPrimary || !request.Request.Contains("EndInvokeDotNet"))
                    {
                        await WriteMessage(brn.StreamWriter, request, !brn.IsPrimary);
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

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                ClientTask?.ContinueWith(t => t.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
                BrowserTask?.ContinueWith(t => t.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
                ProcessMessagesTask?.ContinueWith(t => t.Dispose(), TaskContinuationOptions.ExecuteSynchronously);
            }
        }

        ~IPC()
        {
            Dispose(false);
        }
    }
}
