using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public interface IObservable
{
    Task RegisterObserverAsync(IObserver observer);
    void RemoveObserver(IObserver observer);
}

public interface IObserver
{
    void Update(float temperature);
    Task UpdateHistory(IReadOnlyList<float> temperatureHistory);
}

public class TemperatureData : IObservable
{
    private ConcurrentDictionary<IObserver, Queue<float>> _observers;
    private List<float> _temperatures;
    private object _lock;

    public TemperatureData()
    {
        _observers = new ConcurrentDictionary<IObserver, Queue<float>>();
        _temperatures = new List<float>();
        _lock = new object();
    }

    public async Task RegisterObserverAsync(IObserver observer)
    {
        lock (_lock)
        {
            _observers.TryAdd(observer, new Queue<float>(_temperatures));
        }

        await ProcessPendingTemperatureUpdates(observer);
    }

    private async Task ProcessPendingTemperatureUpdates(IObserver observer)
    {
        float? nextTemperature;

        do
        {
            lock (_lock)
            {
                if (_observers.TryGetValue(observer, out var queue) && queue.Count > 0)
                {
                    nextTemperature = queue.Dequeue();
                }
                else
                {
                    nextTemperature = null;
                }
            }

            if (nextTemperature.HasValue)
            {
                await observer.UpdateHistory(new List<float> { nextTemperature.Value });
            }
        } while (nextTemperature.HasValue);
    }


    public void RemoveObserver(IObserver observer)
    {
        _observers.TryRemove(observer, out _);
    }

    public void NotifyObservers(float temperature)
    {
        foreach (var observer in _observers.Keys)
        {
            _observers[observer].Enqueue(temperature);
        }
    }

    public void AddTemperature(float temperature)
    {
        lock (_lock)
        {
            _temperatures.Add(temperature);
            NotifyObservers(temperature);
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

    public void Update(float temperature)
    {
        DisplayTemperature(temperature);
    }

    public async Task UpdateHistory(IReadOnlyList<float> temperatureHistory)
    {
        await WriteTemperatureHistoryToFileAsync(temperatureHistory);
    }

    private async Task WriteTemperatureHistoryToFileAsync(IEnumerable<float> temperatureHistory)
    {
        string fileName = $"TemperatureHistory_Display{_displayId}.txt";
        using StreamWriter writer = new StreamWriter(fileName, false);
        await writer.WriteLineAsync("Temperature history:");

        foreach (var temperature in temperatureHistory)
        {
            await writer.WriteLineAsync($"Temperature: {temperature}");
        }
    }

    public void DisplayTemperature(float temperature)
    {
        Console.WriteLine($"Temperature: {temperature}");
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
