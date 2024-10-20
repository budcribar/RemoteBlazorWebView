using Grpc.Net.Client;
//using FileWatcher;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using PeakSWC.RemoteWebView;

namespace FileWatcherClientService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly FileWatcherIPC.FileWatcherIPCClient _client;
        

        public Worker(ILogger<Worker> logger, FileWatcherIPC.FileWatcherIPCClient client)
        {
            _logger = logger;
            _client = client;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FileWatcher Client Service is starting.");

            stoppingToken.Register(() => _logger.LogInformation("FileWatcher Client Service is stopping."));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Define the file to watch. You can modify this to read from a config or environment variable.
                    string filePath = Environment.GetEnvironmentVariable("WATCH_FILE_PATH") ?? @"C:\Users\budcr\source\repos\RemoteBlazorWebView\src\Benchmarks\StressServer\publish\StressServer.exe";

                    _logger.LogInformation($"Attempting to watch file: {filePath}");

                    var request = new WatchFileRequest { FilePath = filePath };

                    using var call = _client.WatchFile(request);

                    _logger.LogInformation($"Started watching file: {filePath}");

                    await foreach (var notification in call.ResponseStream.ReadAllAsync(stoppingToken))
                    {
                        _logger.LogInformation("File change detected. Downloading...");

                        // Save the file
                        string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(filePath));

                        using var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);

                        bool isStreaming = false;
                        string runArguments = string.Empty;

                        await foreach (var response in call.ResponseStream.ReadAllAsync(stoppingToken))
                        {
                            if (response.ResponseCase == WatchFileResponse.ResponseOneofCase.Notification)
                            {
                                _logger.LogInformation("File change detected. Preparing to download...");

                                // Update run arguments if needed
                                if (!string.IsNullOrEmpty(response.Notification.RunArguments))
                                {
                                    runArguments = response.Notification.RunArguments;
                                }

                                _logger.LogInformation($"Run arguments set to: {runArguments}");

                                // Reset the file stream for a new file
                                fileStream.SetLength(0);
                                isStreaming = true;
                            }
                            else if (response.ResponseCase == WatchFileResponse.ResponseOneofCase.Chunk && isStreaming)
                            {
                                byte[] bytes = response.Chunk.Content.ToByteArray();
                                await fileStream.WriteAsync(bytes, 0, bytes.Length, stoppingToken);
                                _logger.LogInformation("Received and wrote a 32KB chunk.");
                            }
                        }


                        _logger.LogInformation($"File downloaded to: {tempFilePath}");

                        // Execute the file with arguments
                        ExecuteFile(tempFilePath, runArguments);
                    }
                }
                catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled)
                {
                    _logger.LogInformation("File watching cancelled.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An error occurred while watching the file.");
                    // Optional: Implement retry logic or backoff
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }

            _logger.LogInformation("FileWatcher Client Service has stopped.");
        }

        private void ExecuteFile(string filePath, string arguments)
        {
            try
            {
                string processName = Path.GetFileNameWithoutExtension(filePath);

                _logger.LogInformation($"Checking for existing processes named: {processName}");

                // Retrieve all running processes with the specified name
                var existingProcesses = Process.GetProcessesByName(processName);

                foreach (var process in existingProcesses)
                {
                    try
                    {
                        _logger.LogInformation($"Attempting to close process ID: {process.Id}, Name: {process.ProcessName}");

                        // Attempt to close the main window gracefully
                        if (process.CloseMainWindow())
                        {
                            // Wait for the process to exit gracefully within 5 seconds
                            if (!process.WaitForExit(5000))
                            {
                                _logger.LogWarning($"Process ID: {process.Id} did not exit gracefully. Attempting to kill.");
                                process.Kill(); // Forcefully terminate the process
                                process.WaitForExit(); // Wait indefinitely for the process to exit
                                _logger.LogInformation($"Process ID: {process.Id} has been forcefully terminated.");
                            }
                            else
                            {
                                _logger.LogInformation($"Process ID: {process.Id} has exited gracefully.");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Process ID: {process.Id} does not have a main window or could not receive the close message. Attempting to kill.");
                            process.Kill(); // Forcefully terminate the process
                            process.WaitForExit(); // Wait indefinitely for the process to exit
                            _logger.LogInformation($"Process ID: {process.Id} has been forcefully terminated.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error while attempting to terminate process ID: {process.Id}");
                    }
                }

                _logger.LogInformation($"Executing file: {filePath} with arguments: {arguments}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = filePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(filePath) // Optional: Set the working directory
                };

                var processStart = Process.Start(startInfo);

                if (processStart != null)
                {
                    _logger.LogInformation($"Successfully started process ID: {processStart.Id}, Name: {processStart.ProcessName}");
                }
                else
                {
                    _logger.LogWarning($"Failed to start the process for file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute file: {filePath}");
            }
        }
    }
}
