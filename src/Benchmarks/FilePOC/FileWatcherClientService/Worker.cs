using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using PeakSWC.RemoteWebView;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System.Management;
using System.Linq;

namespace FileWatcherClient
{
    public class Worker
    {
        private readonly ILogger<Worker> _logger;
        private readonly string _fileToWatch;
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
                   
                    _logger.LogError("WatchFilePath is not configured. Please set it in appsettings.json or environment variables.");
                    return;
                   
                }

                _logger.LogInformation("Attempting to watch file: {File}", _fileToWatch);

                var request = new WatchFileRequest { FilePath = _fileToWatch };

                using var call = _client.WatchFile(request,null,null,cancellationToken);

                _logger.LogInformation("Started watching file: {File}", _fileToWatch);

                

                FileStream? fileStream = null;
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
                            // Stop any existing processes before starting
                            StopProcess(Path.GetFileNameWithoutExtension(_tempFilePath));
                            StopProcess("chromedriver");

                            _logger.LogInformation("File change detected. Preparing to download...");

                            string currentRunArguments = _runArguments;

                            if (!string.IsNullOrEmpty(response.Notification.RunArguments))
                            {
                                currentRunArguments = response.Notification.RunArguments;
                                _logger.LogInformation("Run arguments updated to: {Arguments}", currentRunArguments);
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
                                _logger.LogInformation("Created temp file at: {Path}", _tempFilePath);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to create temp file at {Path}", _tempFilePath);
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
                                        await fileStream.WriteAsync(response.Chunk.Content.Memory, cancellationToken);     
                                        _logger.LogInformation("Received and wrote a {SizeKb}KB chunk.", bytes.Length / 1024);
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
                StopProcess("chromedriver");

                _logger.LogInformation("Executing file: {FilePath} with arguments: {Args}", filePath, arguments);

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
                    _logger.LogInformation("Successfully started process ID: {Id}, Name: {Name}", processStart.Id, processStart.ProcessName);
                }
                else
                {
                    _logger.LogWarning("Failed to start the process for file: {File}", filePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute file: {File}", filePath);
            }
        }

        private void StopProcess(string processName)
        {
            _logger.LogInformation("Checking for existing processes named: {Process}", processName);

            // Retrieve all running processes with the specified name
            var existingProcesses = Process.GetProcessesByName(processName);

            foreach (var process in existingProcesses)
            {
                try
                {
                    _logger.LogInformation("Attempting to close process ID: {Id}, Name: {Name}", process.Id, process.ProcessName);

                    // First, terminate child processes
                    var childProcesses = GetChildProcesses(process.Id);
                    foreach (var child in childProcesses)
                    {
                        TerminateProcess(child);
                    }

                    // Attempt to close the parent process gracefully
                    if (process.CloseMainWindow())
                    {
                        // Wait for the process to exit gracefully within 5 seconds
                        if (!process.WaitForExit(5000))
                        {
                            _logger.LogWarning("Process ID: {Id} did not exit gracefully. Attempting to kill.", process.Id);
                            process.Kill(); // Forcefully terminate the process
                            process.WaitForExit(); // Wait indefinitely for the process to exit
                            _logger.LogInformation("Process ID: {Id} has been forcefully terminated.", process.Id);
                        }
                        else
                        {
                            _logger.LogInformation("Process ID: {Id} has exited gracefully.", process.Id);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Process ID: {Id} does not have a main window or could not receive the close message. Attempting to kill.", process.Id);
                        process.Kill(); // Forcefully terminate the process
                        process.WaitForExit(); // Wait indefinitely for the process to exit
                        _logger.LogInformation("Process ID: {Id} has been forcefully terminated.", process.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while attempting to terminate process ID: {Id}", process.Id);
                }
            }
        }

        private List<Process> GetChildProcesses(int parentId)
        {
            var childProcesses = new List<Process>();

            try
            {
                string query = $"Select * From Win32_Process Where ParentProcessId={parentId}";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var results = searcher.Get())
                {
                    foreach (ManagementObject mo in results.Cast<ManagementObject>())
                    {
                        try
                        {
                            int processId = Convert.ToInt32(mo["ProcessId"]);
                            var childProcess = Process.GetProcessById(processId);
                            childProcesses.Add(childProcess);
                            _logger.LogInformation("Found child process: ID={Id}, Name={Name}", childProcess.Id, childProcess.ProcessName);

                            // Recursively find grandchildren
                            childProcesses.AddRange(GetChildProcesses(childProcess.Id));
                        }
                        catch (ArgumentException)
                        {
                            // Process might have exited between the time we got the list and now
                            _logger.LogWarning("Process with ID {Id} no longer exists.", mo["ProcessId"]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving child processes for parent ID: {ParentId}", parentId);
            }

            return childProcesses;
        }

        /// <summary>
        /// Terminates a process and its child processes.
        /// </summary>
        /// <param name="process">Process to terminate</param>
        private void TerminateProcess(Process process)
        {
            try
            {
                _logger.LogInformation("Attempting to terminate child process ID: {Id}, Name: {Name}", process.Id, process.ProcessName);

                // First, terminate any child processes of this process
                var childProcesses = GetChildProcesses(process.Id);
                foreach (var child in childProcesses)
                {
                    TerminateProcess(child);
                }

                // Attempt to close the process gracefully
                if (process.CloseMainWindow())
                {
                    if (!process.WaitForExit(5000))
                    {
                        _logger.LogWarning("Child process ID: {Id} did not exit gracefully. Attempting to kill.", process.Id);
                        process.Kill();
                        process.WaitForExit();
                        _logger.LogInformation("Child process ID: {Id} has been forcefully terminated.", process.Id);
                    }
                    else
                    {
                        _logger.LogInformation("Child process ID: {Id} has exited gracefully.", process.Id);
                    }
                }
                else
                {
                    _logger.LogWarning("Child process ID: {Id} does not have a main window or could not receive the close message. Attempting to kill.", process.Id);
                    process.Kill();
                    process.WaitForExit();
                    _logger.LogInformation("Child process ID: {Id} has been forcefully terminated.", process.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while attempting to terminate child process ID: {Id}", process.Id);
            }
        }
    }
}

