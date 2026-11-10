using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class IPC 
    {
        public const string ScrollMessagePrefix = "mirror-scroll:";
        public const string RestartingMessage = "restarting:";
        private readonly object browserStateLock = new();
        private readonly SemaphoreSlim clientWriteLock = new(1, 1);
        private StringRequest? latestScroll;
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
          
            lock (browserStateLock)
            {
                foreach (var message in messageHistory)
                    channel.Writer.TryWrite(message);
                if (latestScroll != null) channel.Writer.TryWrite(latestScroll);
                observers.TryAdd(brn, channel);
            }
            ProcessMessagesTask = Task.Run(() => ProcessMessages(brn, linkedToken.Token), linkedToken.Token);
        }

        public Task ClientTask { get; }
        public Task BrowserTask { get; }

        public Task? ProcessMessagesTask { get; set; }

        private async Task WriteMessage(IServerStreamWriter<StringRequest> serverStreamWriter, StringRequest message, bool isMirror, CancellationToken cancellationToken)
        {
            try
            {
                await serverStreamWriter.WriteAsync(message, cancellationToken).ConfigureAwait(false);
                if (message.Request.Contains("BeginInvokeJS") && message.Request.Contains("import"))
                {
                    if (isMirror)
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError($"Exception on Write Message {ex.Message}");
            }
        }

        public async ValueTask SendMessage(string message)
        {
            await browserResponseChannel.Writer.WriteAsync(new StringRequest { Request = message }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// True when a browser connection with this clientId is currently attached.
        /// </summary>
        public bool HasConnection(string clientId)
        {
            lock (browserStateLock)
            {
                foreach (var brn in observers.Keys)
                    if (brn.ClientId == clientId) return true;
            }
            return false;
        }

        /// <summary>
        /// Control transfer: make <paramref name="primaryClientId"/> the controlling connection. Flips each attached
        /// connection's primary flag (so render fan-out routing stays correct) and tells every browser who now has
        /// control via a "primaryChanged:" message. Sent straight to each observer, not through message history, so a
        /// later-joining mirror is not replayed a stale hand-off.
        /// </summary>
        public void SetPrimary(string primaryClientId)
        {
            var notice = new StringRequest { Request = $"primaryChanged:{primaryClientId}" };
            lock (browserStateLock)
            {
                foreach (var brn in observers.Keys)
                    brn.IsPrimary = brn.ClientId == primaryClientId;
                foreach (var observer in observers.Values)
                    observer.Writer.TryWrite(notice);
            }
        }

        /// <summary>
        /// Tells every attached browser that the app is about to restart ("restarting:"), so each page waits for the new
        /// copy and comes back by itself. Sent straight to each browser, not through message history.
        /// </summary>
        public void BroadcastRestarting()
        {
            var notice = new StringRequest { Request = RestartingMessage };
            lock (browserStateLock)
            {
                foreach (var observer in observers.Values)
                    observer.Writer.TryWrite(notice);
            }
        }

        /// <summary>
        /// Control transfer: tell every attached browser the current roster — each connection's clientId and whether it
        /// is the owner (the local control surface) or the current controller — so each control bubble can show who is
        /// present and who holds control, and name the other party for a hand-off. <paramref name="excludeClientId"/> is
        /// a connection that is leaving and may not have been removed from the observer set yet.
        /// </summary>
        public void BroadcastParticipants(string ownerClientId, string primaryClientId, string? excludeClientId = null)
        {
            var sb = new StringBuilder("participants:[");
            lock (browserStateLock)
            {
                bool first = true;
                foreach (var brn in observers.Keys.OrderBy(k => k.JoinOrder)) // join order: the first non-owner is the remote user
                {
                    if (brn.ClientId == excludeClientId)
                        continue;
                    if (!first)
                        sb.Append(',');
                    first = false;
                    sb.Append("{\"id\":\"").Append(JsonEncodedText.Encode(brn.ClientId).ToString()).Append('"') // AOT-safe (no reflection serializer)
                      .Append(",\"owner\":").Append(brn.ClientId == ownerClientId ? "true" : "false")
                      .Append(",\"primary\":").Append(brn.ClientId == primaryClientId ? "true" : "false")
                      .Append('}');
                }
                sb.Append(']');
                var notice = new StringRequest { Request = sb.ToString() };
                foreach (var observer in observers.Values)
                    observer.Writer.TryWrite(notice);
            }
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

                        await WriteToClientAsync(m, token).ConfigureAwait(false);

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
                        lock (browserStateLock)
                        {
                            if (m.Request.StartsWith(ScrollMessagePrefix, StringComparison.Ordinal))
                            {
                                // Keep one snapshot for late mirrors, not every wheel tick forever.
                                if (enableMirrors) latestScroll = m;
                            }
                            else if (!m.Request.Contains("EndInvokeDotNet") && enableMirrors)
                                messageHistory.Enqueue(m);
                            foreach (var observer in observers.Values)
                            {
                                observer.Writer.TryWrite(m);
                            }
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
                try
                {
                    await foreach (var request in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                    {
                        if (brn.IsPrimary || !request.Request.Contains("EndInvokeDotNet", StringComparison.Ordinal))
                        {
                            await WriteMessage(brn.StreamWriter, request, !brn.IsPrimary, cancellationToken).ConfigureAwait(false);
                            if (logger.IsEnabled(LogLevel.Information))
                                logger.LogDebug($"WebView -> Browser {request.Id} {request.Request}");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // The browser read ended because the caller cancelled it.
                }
                finally
                {
                    if (observers.TryRemove(brn, out var removed))
                    {
                        removed.Writer.TryComplete();
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
            return WriteToClientAsync(new WebMessageResponse { Response = "location:" + JsonSerializer.Serialize(point, JsonContext.Default.Point) }, cancellationToken);
        }
        public Task SizeChanged(Size size)
        {
            return WriteToClientAsync(new WebMessageResponse { Response = "size:" + JsonSerializer.Serialize(size, JsonContext.Default.Size) }, cancellationToken);
        }

        /// <summary>
        /// Writes one message to the app's stream. Every write to that stream goes through here: a gRPC response stream
        /// accepts one write at a time and throws on a second one that overlaps it, and the stream has several writers
        /// (browser input from the queue, the "browser attached" and "shutdown" notices, window moves). Does nothing
        /// when the app has not opened its stream.
        /// </summary>
        public async Task WriteToClientAsync(WebMessageResponse message, CancellationToken token = default)
        {
            var stream = ClientResponseStream;
            if (stream == null)
                return;

            await clientWriteLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (token.CanBeCanceled)
                    await stream.WriteAsync(message, token).ConfigureAwait(false);
                else
                    await stream.WriteAsync(message).ConfigureAwait(false);
            }
            finally
            {
                clientWriteLock.Release();
            }
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