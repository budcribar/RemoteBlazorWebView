using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
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
        private readonly ConcurrentQueue<StringRequest> messageHistory = new ConcurrentQueue<StringRequest>();
        private readonly ConcurrentDictionary<BrowserResponseNode, Channel<StringRequest>> observers = new();
        private readonly CancellationToken cancellationToken;
        private readonly ILogger<RemoteWebViewService> logger;

        public IServerStreamWriter<WebMessageResponse>? ClientResponseStream { get; set; }
        public void BrowserResponseStream(BrowserResponseNode brn, CancellationTokenSource linkedToken)
        {
            var channel = Channel.CreateUnbounded<StringRequest>();
          
            foreach (var message in messageHistory)
            {
                channel.Writer.TryWrite(message);
            }
     
            observers.TryAdd(brn, channel);
            ProcessMessagesTask = Task.Run(() => ProcessMessages(brn, linkedToken.Token));
        }

        public Task ClientTask { get; }
        public Task BrowserTask { get; }

        public Task? ProcessMessagesTask { get; set; }

        private async Task WriteMessage(IServerStreamWriter<StringRequest> serverStreamWriter, StringRequest message, bool isMirror)
        {
            await serverStreamWriter.WriteAsync(message).ConfigureAwait(false);
            if (message.Request.Contains("BeginInvokeJS") && message.Request.Contains("import"))
            {
                if (isMirror)
                    await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        public async ValueTask SendMessage(string message)
        {
            await browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message }).ConfigureAwait(false);
        }

        public IPC(CancellationToken token, ILogger<RemoteWebViewService> logger, bool enableMirrors)
        {
            cancellationToken = token;
            this.logger = logger;
            ClientTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var m in responseChannel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    {
                        // Serialize the write

                        if (ClientResponseStream != null)
                            await ClientResponseStream.WriteAsync(m).ConfigureAwait(false);

                        if (logger.IsEnabled(LogLevel.Information))
                            logger.LogDebug($"Browser -> WebView {m.Response}");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError($"Client Task has shutdown {ex.Message}");
                }

            }, token);

            BrowserTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (var m in browserResponseChannel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    {
                        if (!m.Request.Contains("EndInvokeDotNet") && enableMirrors)
                            messageHistory.Enqueue(m);
                        foreach (var observer in observers.Values)
                        {
                            observer.Writer.TryWrite(m);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError($"Browser Task has shutdown {ex.Message}");
                }
            }, token);
        }

        private async Task ProcessMessages(BrowserResponseNode brn, CancellationToken cancellationToken)
        {
            if (observers.TryGetValue(brn, out var channel))
            {
                await foreach (var request in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (brn.IsPrimary || !request.Request.Contains("EndInvokeDotNet", StringComparison.Ordinal))
                    {
                        await WriteMessage(brn.StreamWriter, request, !brn.IsPrimary).ConfigureAwait(false);
                        if (logger.IsEnabled(LogLevel.Information))
                            logger.LogDebug($"WebView -> Browser {request.Id} {request.Request}");
                    }
                }
            }
        }

        public ValueTask ReceiveMessage(WebMessageResponse message)
        {
            return responseChannel.Writer.WriteAsync(message,cancellationToken);
        }

        public Task LocationChanged(Point point)
        {
            return (ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "location:" + JsonSerializer.Serialize(point, JsonContext.Default.Point) }) ?? Task.CompletedTask);
        }
        public Task SizeChanged(Size size)
        {
            return (ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "size:" + JsonSerializer.Serialize(size, JsonContext.Default.Size) }) ?? Task.CompletedTask);
        }

        public void Shutdown()
        {
            try
            {
                responseChannel.Writer.Complete();
            }
            catch { }
            try
            {
                browserResponseChannel.Writer.Complete();
            }
            catch { }

        }
    }
}