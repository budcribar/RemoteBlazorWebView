using Grpc.Net.Client;
using FileWatcher;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;

namespace FileWatcherClientService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly FileWatcherService.FileWatcherServiceClient _client;

        public Worker(ILogger<Worker> logger, FileWatcherService.FileWatcherServiceClient client)
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
                    string filePath = Environment.GetEnvironmentVariable("WATCH_FILE_PATH") ?? @"C:\Path\To\Watch\file.exe";

                    var request = new WatchFileRequest { FilePath = filePath };

                    using var call = _client.WatchFile(request);

                    _logger.LogInformation($"Started watching file: {filePath}");

                    await foreach (var notification in call.ResponseStream.ReadAllAsync(stoppingToken))
                    {
                        _logger.LogInformation("File change detected. Downloading...");

                        // Save the file
                        string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(filePath));
                        await File.WriteAllBytesAsync(tempFilePath, notification.FileContent.ToByteArray(), stoppingToken);

                        _logger.LogInformation($"File downloaded to: {tempFilePath}");

                        // Execute the file with arguments
                        ExecuteFile(tempFilePath, notification.RunArguments);
                    }
                }
                catch (Grpc.Core.RpcException rpcEx) when (rpcEx.StatusCode == Grpc.Core.StatusCode.Cancelled)
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
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = filePath,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };

                process.Start();
                _logger.LogInformation($"Executed file: {filePath} with arguments: {arguments}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute file: {filePath}");
            }
        }
    }
}
