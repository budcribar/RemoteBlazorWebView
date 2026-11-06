using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace PeakSWC.RemoteBlazorWebView.RemoteComponents;

/// <summary>
/// The component process's side of a remote component: holds the contract values the app sent and sends its callbacks. The
/// <c>[RemoteComponentContract]</c> source generator writes a subclass per contract that implements the contract interface; the
/// component's UI injects it and re-renders on <see cref="Changed"/>.
/// </summary>
public abstract class RemoteComponentReceiverBase(RemoteComponentConnection connection)
{
    private int _started, _received;

    public RemoteComponentConnection Connection { get; } = connection;

    /// <summary>Some values changed. Raised on the reading thread: use the component's InvokeAsync.</summary>
    public event Action? Changed;

    /// <summary>The app has sent values at least once (until then every value is its default).</summary>
    public bool HasValues => Volatile.Read(ref _received) != 0;

    /// <summary>A value the app sent couldn't be read as the contract's type: name and error. The value keeps what it had.</summary>
    public event Action<string, Exception>? BadValue;

    /// <summary>Starts reading the app's messages in the background (once).</summary>
    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = Task.Run(() => Connection.RunAsync(OnMessageAsync));
    }

    /// <summary>Applies one message (what <see cref="Start"/> does for each line); public for tests.</summary>
    public Task OnMessageAsync(RemoteComponentMessage message)
    {
        if (message.Set is not { } set) return Task.CompletedTask;
        var any = false;
        foreach (var (name, value) in set)
        {
            try { any |= Apply(name, value); }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException) { BadValue?.Invoke(name, ex); }
        }
        Volatile.Write(ref _received, 1);
        if (any) Changed?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>Sets the named value (generated); false for a name the contract doesn't have.</summary>
    protected abstract bool Apply(string name, JsonElement value);

    /// <summary>Sends a callback with no argument.</summary>
    protected Task SendCallAsync(string name) =>
        Connection.SendAsync(new RemoteComponentMessage { Call = name });

    /// <summary>Sends a callback with one argument.</summary>
    protected Task SendCallAsync<T>(string name, T arg) =>
        Connection.SendAsync(new RemoteComponentMessage { Call = name, Arg = JsonSerializer.SerializeToElement(arg, RemoteComponentJson.Options) });

    /// <summary>Reads a value the app sent as <typeparamref name="T"/>.</summary>
    protected static T Read<T>(JsonElement value) => value.Deserialize<T>(RemoteComponentJson.Options)!;
}

public static class RemoteComponentServiceCollectionExtensions
{
    /// <summary>Registers the connection and the generated receiver <typeparamref name="TReceiver"/> (also as each interface it
    /// implements, i.e. the contract), started on first use.</summary>
    public static IServiceCollection AddRemoteComponentReceiver<TReceiver>(this IServiceCollection services, RemoteComponentConnection connection)
        where TReceiver : RemoteComponentReceiverBase
    {
        services.AddSingleton(connection);
        services.AddSingleton(sp =>
        {
            var receiver = ActivatorUtilities.CreateInstance<TReceiver>(sp, connection);
            receiver.Start();
            return receiver;
        });
        foreach (var contract in typeof(TReceiver).GetInterfaces())
            services.AddSingleton(contract, sp => sp.GetRequiredService<TReceiver>());
        return services;
    }
}
