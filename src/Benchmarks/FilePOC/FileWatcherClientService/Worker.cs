using Grpc.Net.Client;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
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
        private readonly IConfiguration _configuration;

        public Worker(ILogger<Worker> logger, FileWatcherIPC.FileWatcherIPCClient client, IConfiguration configuration)
        {
            _logger = logger;
            _client = client;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FileWatcher Client Service is starting.");

            stoppingToken.Register(() => _logger.LogInformation("FileWatcher Client Service is stopping."));

            try
            {
                // Read configurations from appsettings.json or environment variables
                string filePath = _configuration["FileWatcher:WatchFilePath"];
                string runArguments = _configuration["FileWatcher:RunArguments"];

                if (string.IsNullOrEmpty(filePath))
                {
                    filePath = @"C:\Users\budcr\source\repos\RemoteBlazorWebView\src\Benchmarks\StressServer\publish\StressServer.exe";
                    _logger.LogError("WatchFilePath is not configured. Please set it in appsettings.json or environment variables.");
                }

                _logger.LogInformation($"Attempting to watch file: {filePath}");

                var request = new WatchFileRequest { FilePath = filePath };

                using var call = _client.WatchFile(request);

                _logger.LogInformation($"Started watching file: {filePath}");

                string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(filePath));

                StopProcess(Path.GetFileNameWithoutExtension(tempFilePath));

                FileStream fileStream = null;
                bool isStreaming = false;

                await foreach (var response in call.ResponseStream.ReadAllAsync(stoppingToken))
                {
                    switch (response.ResponseCase)
                    {
                        case WatchFileResponse.ResponseOneofCase.Notification:
                            _logger.LogInformation("File change detected. Preparing to download...");

                            // Update run arguments if provided
                            if (!string.IsNullOrEmpty(response.Notification.RunArguments))
                            {
                                runArguments = response.Notification.RunArguments;
                            }

                            _logger.LogInformation($"Run arguments set to: {runArguments}");

                            // Dispose previous FileStream if any
                            if (fileStream != null)
                            {
                                await fileStream.FlushAsync(stoppingToken);
                                fileStream.Dispose();
                            }

                            // Create a new FileStream for the incoming file
                            fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                            isStreaming = true;
                            break;

                        case WatchFileResponse.ResponseOneofCase.Chunk:
                            if (isStreaming && fileStream != null)
                            {
                                // Check for zero-length chunk indicating end of transfer
                                if (response.Chunk.Content.Length == 0)
                                {
                                    _logger.LogInformation("File transfer complete.");

                                    // Flush and dispose the FileStream
                                    await fileStream.FlushAsync(stoppingToken);
                                    fileStream.Dispose();
                                    fileStream = null;
                                    isStreaming = false;

                                    // Execute the file after transfer
                                    ExecuteFile(tempFilePath, runArguments);
                                }
                                else
                                {
                                    // Convert ByteString to byte array
                                    byte[] bytes = response.Chunk.Content.ToByteArray();

                                    // Asynchronously write bytes to the file
                                    await fileStream.WriteAsync(bytes, 0, bytes.Length, stoppingToken);

                                    _logger.LogInformation($"Received and wrote a {bytes.Length / 1024}KB chunk.");
                                }
                            }
                            else
                            {
                                _logger.LogWarning("Received a chunk without an active file stream.");
                            }
                            break;
                    }
                }

                _logger.LogInformation("File watching completed.");

                // Ensure the last FileStream is properly closed
                if (fileStream != null)
                {
                    await fileStream.FlushAsync(stoppingToken);
                    fileStream.Dispose();
                    fileStream = null;
                }

                _logger.LogInformation("FileWatcher Client Service has stopped.");
            }
            catch (Grpc.Core.RpcException rpcEx) when (rpcEx.StatusCode == Grpc.Core.StatusCode.Cancelled)
            {
                _logger.LogInformation("File watching cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while watching the file.");
                // Implement retry logic or backoff if necessary
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private void ExecuteFile(string filePath, string arguments)
        {
            try
            {
                string processName = Path.GetFileNameWithoutExtension(filePath);

                StopProcess(processName);

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

        private void StopProcess(string processName)
        {
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
        }
    }
}
