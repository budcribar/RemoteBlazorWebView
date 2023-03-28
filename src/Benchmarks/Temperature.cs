using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

public interface IObservable
{
    Task RegisterObserverAsync(IObserver observer);
    void RemoveObserver(IObserver observer);
}

public interface IObserver
{
    Task UpdateHistory(float temperature);
}

public class TemperatureData : IObservable
{
    private ConcurrentDictionary<IObserver, BlockingCollection<float>> _observers;
    private List<float> _temperatures;
    private readonly object _lock = new object();

    public TemperatureData()
    {
        _observers = new ConcurrentDictionary<IObserver, BlockingCollection<float>>();
        _temperatures = new List<float>();
    }

    public async Task RegisterObserverAsync(IObserver observer)
    {
        BlockingCollection<float> observerTemperatures;

        lock (_lock)
        {
            observerTemperatures = new BlockingCollection<float>();

            foreach (var temperature in _temperatures)
            {
                observerTemperatures.Add(temperature);
            }

            _observers.TryAdd(observer, observerTemperatures);
        }

        await ProcessPendingTemperatureUpdates(observer);
    }

    private async Task ProcessPendingTemperatureUpdates(IObserver observer)
    {
        if (_observers.TryGetValue(observer, out var updates))
        {
            foreach (float temperature in updates.GetConsumingEnumerable())
            {
                await observer.UpdateHistory(temperature);
            }
        }
    }

    public void RemoveObserver(IObserver observer)
    {
        if (_observers.TryRemove(observer, out var updates))
        {
            updates.CompleteAdding();
        }
    }

    public void AddTemperature(float temperature)
    {
        lock (_lock)
        {
            _temperatures.Add(temperature);

            foreach (var observer in _observers.Keys)
            {
                _observers[observer].Add(temperature);
            }
        }
    }
}

public class TemperatureDisplay : IObserver
{
    private TemperatureData _temperatureData;
    private int _displayId;

    public async Task RegisterAsync(TemperatureData temperatureData, int displayId)
    {
        _temperatureData = temperatureData;
        _displayId = displayId;
        await _temperatureData.RegisterObserverAsync(this);
    }

    public async Task UpdateHistory(float temperature)
    {
        await WriteTemperatureToFileAsync(temperature);
    }

    private async Task WriteTemperatureToFileAsync(float temperature)
    {
        string fileName = $"TemperatureHistory_Display{_displayId}.txt";
        using StreamWriter writer = new StreamWriter(fileName, true);
        await writer.WriteLineAsync($"Temperature: {temperature}");
    }
}

public class Program
{
    public static async Task Main(string[] args)
    {
        TemperatureData temperatureData = new TemperatureData();
        TemperatureDisplay display1 = new TemperatureDisplay();
        await display1.RegisterAsync(temperatureData, 1);
        TemperatureDisplay display2 = new TemperatureDisplay();
        await display2.RegisterAsync(temperatureData, 2);

        temperatureData.AddTemperature(25.5f);
        temperatureData.AddTemperature(27.3f);
        temperatureData.AddTemperature(22.8f);
    }
}
