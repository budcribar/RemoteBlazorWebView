using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using PeakSWC.RemoteWebView;

using Microsoft.Extensions.Logging;

namespace FileWatcherClientService
{
    public class Worker
    {
        private readonly ILogger<Worker> _logger;
        private string _fileToWatch;
        private readonly string _runArguments;
        private readonly string _tempFilePath;
        private readonly FileWatcherIPC.FileWatcherIPCClient _client;

        public Worker(ILogger<Worker> logger, string fileToWatch, string runArguments, FileWatcherIPC.FileWatcherIPCClient client)
        {
            _logger = logger;
            _fileToWatch = fileToWatch;
            _runArguments = runArguments;
            _client = client;
            _tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetFileName(_fileToWatch));
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("FileWatcher Client Service is starting.");

            try
            {
                if (string.IsNullOrEmpty(_fileToWatch))
                {
                    _fileToWatch = @"C:\Users\budcr\source\repos\RemoteBlazorWebView\src\Benchmarks\StressServer\publish\StressServer.exe";
                    _logger.LogError("WatchFilePath is not configured. Please set it in appsettings.json or environment variables.");
                   
                }

                if (!File.Exists(_fileToWatch))
                {
                    _logger.LogError($"File {_fileToWatch} does not exist. Please check the path.");
                    return;
                }

                _logger.LogInformation($"Attempting to watch file: {_fileToWatch}");

                var request = new WatchFileRequest { FilePath = _fileToWatch };

                using var call = _client.WatchFile(request);

                _logger.LogInformation($"Started watching file: {_fileToWatch}");

                // Stop any existing processes before starting
                StopProcess(Path.GetFileNameWithoutExtension(_tempFilePath));

                FileStream fileStream = null;
                bool isStreaming = false;

                await foreach (var response in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _logger.LogInformation("Cancellation requested. Exiting ExecuteAsync.");
                        break;
                    }

                    switch (response.ResponseCase)
                    {
                        case WatchFileResponse.ResponseOneofCase.Notification:
                            _logger.LogInformation("File change detected. Preparing to download...");

                            string currentRunArguments = _runArguments;

                            if (!string.IsNullOrEmpty(response.Notification.RunArguments))
                            {
                                currentRunArguments = response.Notification.RunArguments;
                                _logger.LogInformation($"Run arguments updated to: {currentRunArguments}");
                            }

                            // Dispose previous FileStream if any
                            if (fileStream != null)
                            {
                                await fileStream.FlushAsync(cancellationToken);
                                fileStream.Dispose();
                                fileStream = null;
                            }

                            // Create a new FileStream for the incoming file
                            try
                            {
                                fileStream = new FileStream(_tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                                isStreaming = true;
                                _logger.LogInformation($"Created temp file at: {_tempFilePath}");
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, $"Failed to create temp file at {_tempFilePath}");
                            }
                            break;

                        case WatchFileResponse.ResponseOneofCase.Chunk:
                            if (isStreaming && fileStream != null)
                            {
                                // Check for zero-length chunk indicating end of transfer
                                if (response.Chunk.Content.Length == 0)
                                {
                                    _logger.LogInformation("File transfer complete.");

                                    // Flush and dispose the FileStream
                                    try
                                    {
                                        await fileStream.FlushAsync(cancellationToken);
                                        fileStream.Dispose();
                                        fileStream = null;
                                        isStreaming = false;

                                        // Execute the file after transfer
                                        ExecuteFile(_tempFilePath, _runArguments);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error while finalizing the file transfer.");
                                    }
                                }
                                else
                                {
                                    // Convert ByteString to byte array
                                    byte[] bytes = response.Chunk.Content.ToByteArray();

                                    // Asynchronously write bytes to the file
                                    try
                                    {
                                        await fileStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                                        _logger.LogInformation($"Received and wrote a {bytes.Length / 1024}KB chunk.");
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error while writing to the file stream.");
                                    }
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
                    try
                    {
                        await fileStream.FlushAsync(cancellationToken);
                        fileStream.Dispose();
                        fileStream = null;
                        _logger.LogInformation("Final file stream disposed.");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error while disposing the final file stream.");
                    }
                }

                _logger.LogInformation("FileWatcher Client Service has stopped.");
            }
            catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled)
            {
                _logger.LogInformation("File watching cancelled.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while watching the file.");
                // Implement retry logic or backoff if necessary
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                // Consider implementing a loop or using Polly for more robust retry mechanisms
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
                    UseShellExecute = true, // Allows GUI applications to create windows
                    CreateNoWindow = false,  // Set to true if you want to hide the window
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
