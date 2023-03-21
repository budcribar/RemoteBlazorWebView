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
    private ConcurrentDictionary<IObserver, byte> _observers;
    private List<float> _temperatures;
    private object _temperatureLock;

    public TemperatureData()
    {
        _observers = new ConcurrentDictionary<IObserver, byte>();
        _temperatures = new List<float>();
        _temperatureLock = new object();
    }

    public async Task RegisterObserverAsync(IObserver observer)
    {
        IReadOnlyList<float> temperatureData;

        lock (_temperatureLock)
        {
            _observers.TryAdd(observer, 0);
            temperatureData = _temperatures.ToList().AsReadOnly();
        }

        await observer.UpdateHistory(temperatureData);
    }

    public void RemoveObserver(IObserver observer)
    {
        _observers.TryRemove(observer, out _);
    }

    public void NotifyObservers(float temperature)
    {
        foreach (var observer in _observers.Keys)
        {
            observer.Update(temperature);
        }
    }

    public void AddTemperature(float temperature)
    {
        lock (_temperatureLock)
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