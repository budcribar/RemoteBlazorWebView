using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeakSWC.RemoteBlazorWebView.RemoteComponents;

/// <summary>The JSON options both ends use for contract values.</summary>
public static class RemoteComponentJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// One line of the remote component protocol. The app sends <see cref="Set"/> (contract values by name, only those that changed); the
/// component's process sends <see cref="Call"/> with an optional <see cref="Arg"/> (a callback). Anything else is ignored.
/// </summary>
public sealed class RemoteComponentMessage
{
    [JsonPropertyName("set")] public Dictionary<string, JsonElement>? Set { get; init; }
    [JsonPropertyName("call")] public string? Call { get; init; }
    [JsonPropertyName("arg")] public JsonElement? Arg { get; init; }

    public string ToLine() => JsonSerializer.Serialize(this, LineOptions);

    /// <summary>The message on <paramref name="line"/>, or null when it isn't one.</summary>
    public static RemoteComponentMessage? Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try { return JsonSerializer.Deserialize<RemoteComponentMessage>(line, LineOptions); }
        catch (JsonException) { return null; }
    }

    private static readonly JsonSerializerOptions LineOptions = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}

/// <summary>
/// The app's or the component process's end of the protocol: newline-delimited <see cref="RemoteComponentMessage"/>s. In the app, the
/// writer is the child's stdin and the reader its stdout; in the child (<c>RemoteComponentHost</c>), the other way round. Either may be
/// null when that direction isn't used.
/// </summary>
public sealed class RemoteComponentConnection(TextReader? input, TextWriter? output) : IDisposable
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _disposed;

    /// <summary>Sends one message; false when the other end has gone away (or there is no output).</summary>
    public async Task<bool> SendAsync(RemoteComponentMessage message, CancellationToken cancellationToken = default)
    {
        if (output is null || Volatile.Read(ref _disposed) != 0) return false;
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await output.WriteLineAsync(message.ToLine().AsMemory(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { return false; }
        finally { _writeGate.Release(); }
    }

    /// <summary>Reads messages until the input ends (the other end closed it or went away), passing each to <paramref name="onMessage"/>.
    /// Lines that aren't messages are skipped; a handler that throws doesn't stop the reading.</summary>
    public async Task RunAsync(Func<RemoteComponentMessage, Task> onMessage, CancellationToken cancellationToken = default)
    {
        if (input is null) return;
        try
        {
            while (await input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (RemoteComponentMessage.Parse(line) is not { } message) continue;
                try { await onMessage(message).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException) { HandlerFailed?.Invoke(ex); }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        Closed?.Invoke();
    }

    /// <summary>The input ended.</summary>
    public event Action? Closed;

    /// <summary>A message handler threw (the message is dropped; reading goes on).</summary>
    public event Action<Exception>? HandlerFailed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { output?.Dispose(); } catch { }
        try { input?.Dispose(); } catch { }
    }
}

/// <summary>A started remote component: the address the app frames, and the connection to its process.</summary>
public sealed class RemoteComponentSession(Uri source, RemoteComponentConnection connection, Func<ValueTask>? stop = null) : IAsyncDisposable
{
    /// <summary>The page to frame (the component process's RemoteBlazorWebView page on the relay).</summary>
    public Uri Source { get; } = source;
    public RemoteComponentConnection Connection { get; } = connection;
    private int _disposed;

    /// <summary>Closes the connection and stops the component's process (the <c>stop</c> given when the session was made).</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Connection.Dispose();
        if (stop is not null) await stop().ConfigureAwait(false);
    }
}
