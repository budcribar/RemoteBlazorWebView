using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using PeakSWC.RemoteBlazorWebView.RemoteComponents;

namespace RemoteBlazorWebView.RemoteComponents.Tests;

/// <summary>A hand-written frame for a contract { string Zone; int Count; Task Picked(int) } (what the generator writes).</summary>
public sealed class ClockFrame : RemoteComponentFrameBase
{
    [Parameter] public string Zone { get; set; } = "";
    [Parameter] public int Count { get; set; }
    [Parameter] public EventCallback<int> Picked { get; set; }

    protected override IEnumerable<(string Name, object? Value, Type Type)> GetContractValues() =>
        [("Zone", Zone, typeof(string)), ("Count", Count, typeof(int))];

    protected override Task OnCallAsync(string name, JsonElement? arg) => name switch
    {
        "Picked" => Picked.InvokeAsync(arg is { } a ? a.Deserialize<int>(RemoteComponentJson.Options) : default),
        _ => Task.CompletedTask,
    };
}

public sealed class ClockReceiver(RemoteComponentConnection connection) : RemoteComponentReceiverBase(connection)
{
    public string Zone { get; private set; } = "";
    public int Count { get; private set; }
    public Task Picked(int value) => SendCallAsync("Picked", value);

    protected override bool Apply(string name, JsonElement value)
    {
        switch (name)
        {
            case "Zone": Zone = Read<string>(value); return true;
            case "Count": Count = Read<int>(value); return true;
            default: return false;
        }
    }
}

/// <summary>Two in-memory pipes: what the app writes the child reads, and the other way round.</summary>
public sealed class Wire : IAsyncDisposable
{
    private readonly Pipe _toChild = new(), _toApp = new();
    public RemoteComponentConnection App { get; }
    public RemoteComponentConnection Child { get; }
    public ConcurrentQueue<RemoteComponentMessage> SentToChild { get; } = new();

    public Wire()
    {
        App = new RemoteComponentConnection(new StreamReader(_toApp.Reader.AsStream()), new RecordingStreamWriter(_toChild.Writer.AsStream(), SentToChild));
        Child = new RemoteComponentConnection(new StreamReader(_toChild.Reader.AsStream()), new StreamWriter(_toApp.Writer.AsStream()));
    }

    public async ValueTask DisposeAsync()
    {
        App.Dispose();
        Child.Dispose();
        await _toChild.Writer.CompleteAsync();
        await _toApp.Writer.CompleteAsync();
        await _toChild.Reader.CompleteAsync();
        await _toApp.Reader.CompleteAsync();
    }

    private sealed class RecordingStreamWriter(Stream stream, ConcurrentQueue<RemoteComponentMessage> recorded) : StreamWriter(stream)
    {
        public override Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (RemoteComponentMessage.Parse(new string(buffer.Span)) is { } message)
                recorded.Enqueue(message);
            return base.WriteLineAsync(buffer, cancellationToken);
        }

        public override Task WriteLineAsync(string? value)
        {
            if (RemoteComponentMessage.Parse(value) is { } message)
                recorded.Enqueue(message);
            return base.WriteLineAsync(value);
        }
    }
}
