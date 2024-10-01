//using FileSyncServer.Tests;
using System.Diagnostics;

using FileSyncServer;

public class ServerFixture : IDisposable
{
    public Process ServerProcess { get; private set; }
    private readonly string _serverExePath;
    private readonly ManualResetEventSlim _serverReady = new(false);

    public ServerFixture()
    {
        Utility.KillExistingProcesses("Server");
        // Determine the path to the server executable
        var testOutputPath = Directory.GetCurrentDirectory();
        _serverExePath = Path.Combine(testOutputPath, "Server.exe"); // Adjust if necessary

        if (!File.Exists(_serverExePath))
        {
            throw new FileNotFoundException($"Server executable not found at path: {_serverExePath}");
        }

        // Configure the server process start information
        var processStartInfo = new ProcessStartInfo
        {
            FileName = _serverExePath,
            Arguments = "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        ServerProcess = new Process
        {
            StartInfo = processStartInfo,
            EnableRaisingEvents = true
        };

        // Handle output data to detect when the server is ready
        ServerProcess.OutputDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Console.WriteLine($"Server Output: {args.Data}");
                if (args.Data.Contains("Now listening on: https://"))
                {
                    _serverReady.Set();
                }
            }
        };

        ServerProcess.ErrorDataReceived += (sender, args) =>
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                Console.WriteLine($"Server Error: {args.Data}");
            }
        };

        // Start the server process
        ServerProcess.Start();
        ServerProcess.BeginOutputReadLine();
        ServerProcess.BeginErrorReadLine();

        // Wait until the server is ready or timeout after 10 seconds
        if (!_serverReady.Wait(10000))
        {
            throw new TimeoutException("Server did not start listening on https://localhost:5001 within the expected time.");
        }
        Console.WriteLine("Server started...");
    }

    public void Dispose()
    {
        if (!ServerProcess.HasExited)
        {
            ServerProcess.Kill();
            ServerProcess.WaitForExit();
        }
        ServerProcess.Dispose();
        _serverReady.Dispose();
    }
}
