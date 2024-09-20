using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace StressServer
{
    using Google.Protobuf.WellKnownTypes;
    using Grpc.Net.Client;
    using PeakSWC.RemoteWebView;
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;

    public static class ExecutableManager
    {
        private static async Task<bool> WaitForClientToConnectAsync(string clientId, GrpcChannel channel, int timeoutMs = 3000, int checkIntervalMs = 100)
        {
            var client = new WebViewIPC.WebViewIPCClient(channel);
            int elapsedMs = 0;

            while (elapsedMs < timeoutMs)
            {
                try
                {
                    var response = await client.GetIdsAsync(new Empty());
                    var idsSet = new HashSet<string>(response.Responses);

                    // Check if the client ID is present
                    if (idsSet.Contains(clientId))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Logging.LogEvent($"gRPC call failed: {ex.Message}", EventLogEntryType.Error);
                    return false;
                }

                await Task.Delay(checkIntervalMs);
                elapsedMs += checkIntervalMs;
            }

            // Timeout reached without registering the client ID
            Logging.LogEvent($"Timeout waiting for client ID '{clientId}' to register.", EventLogEntryType.Error);
            return false;
        }
        /// <param name="clientId">The client ID to wait for.</param>
        /// <param name="channel">gRPC channel for communication.</param>
        /// <param name="arguments">Arguments to pass to the executable.</param>
        /// <param name="startupDelayMs">Delay before starting the next process (default: 50ms).</param>
        /// <param name="initializationTimeoutMs">Maximum time to wait for client ID (default: 3000ms).</param>
        /// <returns>The started and validated Process instance.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the executable is not found.</exception>
        /// <exception cref="Exception">Thrown if the process fails to start or register the client ID.</exception>
        public static async Task<Process> RunExecutableAsync(string exePath, string clientId, GrpcChannel channel, params string[] arguments)
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Executable not found.", exePath);

            // Configure the process start information
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            // Add provided arguments to the process
            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process? process = null;

            try
            {
                // Initialize the process
                process = new Process { StartInfo = startInfo };

                // Start the process
                bool started = process.Start();
                if (!started)
                {
                    throw new Exception("Failed to start the process.");
                }

                // Optional: Set the process priority
                try
                {
                    process.PriorityClass = ProcessPriorityClass.High;
                }
                catch (Exception priorityEx)
                {
                    Logging.LogEvent($"Failed to set process priority. Exception: {priorityEx.Message}", EventLogEntryType.Warning);
                    // Proceed without setting priority or choose a different priority level
                }

                // Wait for the client ID to be registered
                bool isClientConnected = await WaitForClientToConnectAsync(
                    clientId: clientId,
                    channel: channel,
                    timeoutMs: 3000, // 3 seconds timeout
                    checkIntervalMs: 100 // Check every 100ms
                );

                // Verify the client ID is registered
                if (isClientConnected)
                {
                    return process; // Successfully launched and validated
                }
                else
                {
                    throw new Exception($"Client ID '{clientId}' was not registered within the timeout period.");
                }
            }
            catch (Exception ex)
            {
                Logging.LogEvent($"Failed to launch executable '{exePath}'. Exception: {ex.Message}", EventLogEntryType.Error);

                // Attempt to kill the process if it's running
                if (process != null && !process.HasExited)
                {
                    try
                    {
                        process.Kill();
                        Logging.LogEvent($"Process PID {process.Id} was terminated due to failure.", EventLogEntryType.Warning);
                    }
                    catch (Exception killEx)
                    {
                        Logging.LogEvent($"Failed to kill process PID {process.Id}. Exception: {killEx.Message}", EventLogEntryType.Error);
                        // Continue to throw the original exception
                    }
                }

                throw; // Re-throw the exception to be handled by the caller
            }
        }
        private const string ResourceName = "StressServer.RemoteBlazorWebViewTutorial.WpfApp.exe";

        /// <summary>
        /// Extracts the embedded executable to a temporary directory.
        /// </summary>
        /// <returns>The full path to the extracted executable.</returns>
        public static string ExtractExecutable()
        {
            string tempPath = Path.Combine(Path.GetTempPath(), "RemoteBlazorWebViewTutorial.WpfApp");
            Directory.CreateDirectory(tempPath);

            string exePath = Path.Combine(tempPath, "RemoteBlazorWebViewTutorial.WpfApp.exe");

            using (Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException("Embedded resource not found.", ResourceName);

                using (FileStream fileStream = new FileStream(exePath, FileMode.Create, FileAccess.Write))
                {
                    stream.CopyTo(fileStream);
                }
            }

            return exePath;
        }
        

        /// <summary>
        /// Deletes the extracted executable and its directory.
        /// </summary>
        /// <param name="exePath">The full path to the executable.</param>
        public static void CleanUp(string exePath)
        {
            try
            {
                if (File.Exists(exePath))
                {
                    File.Delete(exePath);
                }

                string directory = Path.GetDirectoryName(exePath) ?? string.Empty;
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Cleanup failed: {ex.Message}");
            }
        }
    }
}

