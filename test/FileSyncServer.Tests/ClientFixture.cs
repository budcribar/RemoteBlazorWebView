using FileSyncServer;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Xunit;

public class ClientFixture : IDisposable
{
    public Process ClientProcess { get; private set; }
    public Guid ClientId { get; private set; } // Exposed ClientId
    private readonly string _clientExePath;
    private readonly ManualResetEventSlim _clientReady = new(false);

    public ClientFixture()
    {
        // Kill any existing Client processes to ensure a clean start
        // Utilities.KillExistingProcesses("Client");

        // Determine the path to the client executable
        var testOutputPath = Directory.GetCurrentDirectory();
        _clientExePath = Path.Combine(testOutputPath, "Client.exe"); // Adjust if necessary

        if (!File.Exists(_clientExePath))
        {
            throw new FileNotFoundException($"Client executable not found at path: {_clientExePath}");
        }

        // Generate a new clientId (GUID)
        ClientId = Guid.NewGuid();

        // Configure the client process start information with the clientId as an argument
        var processStartInfo = new ProcessStartInfo
        {
            FileName = _clientExePath,
            Arguments = ClientId.ToString(), // Pass clientId as argument
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ClientProcess = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };

        // Handle output data to detect when the client is ready
        ClientProcess.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Console.WriteLine($"Client Output: {args.Data}");
                if (args.Data.Contains("File synchronization initiated."))
                {
                    _clientReady.Set();
                }
            }
        };

        // Handle error data
        ClientProcess.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Console.WriteLine($"Client Error: {args.Data}");
            }
        };

        // Start the client process
        ClientProcess.Start();
        ClientProcess.BeginOutputReadLine();
        ClientProcess.BeginErrorReadLine();

        // Wait until the client is ready or timeout after 10 seconds
        if (!_clientReady.Wait(10000))
        {
            throw new TimeoutException("Client did not start File synchronization within the expected time.");
        }

        // Optional small delay to ensure all initialization steps are complete
        //Task.Delay(100).Wait();
        Console.WriteLine($"Client started with clientId: {ClientId}");
    }

    public void Dispose()
    {
        if (!ClientProcess.HasExited)
        {
            ClientProcess.Kill();
            ClientProcess.WaitForExit();
        }
        ClientProcess.Dispose();
        _clientReady.Dispose();
    }
}
