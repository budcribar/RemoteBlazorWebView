using System.Collections.Concurrent;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;

namespace StaticContentCache.Tests;

/// <summary>
/// The app's stream has several writers (browser input, the "browser attached" and "shutdown" notices, window moves) and,
/// like a real gRPC response stream, accepts one write at a time. Overlapping writes used to throw; when the input loop
/// was the one that threw, it ended and no input reached the app again.
/// </summary>
public class ClientStreamWriteTests
{
    [Fact]
    public async Task Notices_and_browser_input_never_write_to_the_app_at_the_same_time()
    {
        using var cancel = new CancellationTokenSource();
        var ipc = new IPC(cancel.Token, NullLogger<RemoteWebViewService>.Instance, enableMirrors: true);
        var app = new OneAtATimeWriter();
        ipc.ClientResponseStream = app;

        var writers = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            writers.Add(ipc.ReceiveMessage(new WebMessageResponse { Response = $"input{i}" }).AsTask());
            writers.Add(ipc.WriteToClientAsync(new WebMessageResponse { Response = "browserAttached:" }));
            writers.Add(ipc.SizeChanged(new System.Drawing.Size(i, i)));
        }
        await Task.WhenAll(writers);

        var until = DateTime.UtcNow.AddSeconds(10);
        while (app.Messages.Count < 60 && DateTime.UtcNow < until) await Task.Delay(20);

        Assert.Equal(0, app.Overlaps);
        Assert.Equal(60, app.Messages.Count);
        // Browser input still arrives in the order it was sent.
        Assert.Equal(Enumerable.Range(0, 20).Select(i => $"input{i}"), app.Messages.Where(m => m.StartsWith("input", StringComparison.Ordinal)));
        Assert.False(ipc.ClientTask.IsCompleted); // the input loop is still running
        cancel.Cancel();
    }

    [Fact]
    public async Task A_notice_before_the_app_has_opened_its_stream_does_nothing()
    {
        using var cancel = new CancellationTokenSource();
        var ipc = new IPC(cancel.Token, NullLogger<RemoteWebViewService>.Instance, enableMirrors: false);

        await ipc.WriteToClientAsync(new WebMessageResponse { Response = "browserAttached:" });
        cancel.Cancel();
    }

    /// <summary>Behaves like gRPC's response stream: a write that starts while another is in progress throws.</summary>
    private sealed class OneAtATimeWriter : IServerStreamWriter<WebMessageResponse>
    {
        private readonly ConcurrentQueue<string> messages = new();
        private int writing;
        private int overlaps;
        public IReadOnlyCollection<string> Messages => messages;
        public int Overlaps => overlaps;
        public WriteOptions? WriteOptions { get; set; }

        public async Task WriteAsync(WebMessageResponse message)
        {
            if (Interlocked.Exchange(ref writing, 1) == 1)
            {
                Interlocked.Increment(ref overlaps);
                throw new InvalidOperationException("Can't write the message because the previous write is in progress.");
            }
            try
            {
                await Task.Delay(5);
                messages.Enqueue(message.Response);
            }
            finally
            {
                Volatile.Write(ref writing, 0);
            }
        }

        public Task WriteAsync(WebMessageResponse message, CancellationToken cancellationToken) => WriteAsync(message);
    }
}
