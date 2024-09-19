using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace StressServer
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;

    public static class ExecutableManager
    {
        /// <summary>
        /// Launches an executable with specified arguments. If the executable fails to initialize within 1 second,
        /// it will attempt to relaunch it up to a maximum of 3 retries.
        /// </summary>
        /// <param name="exePath">The full path to the executable.</param>
        /// <param name="arguments">Arguments to pass to the executable.</param>
        /// <returns>The Process object of the launched executable.</returns>
        /// <exception cref="FileNotFoundException">Thrown if the executable is not found at the specified path.</exception>
        /// <exception cref="Exception">Thrown if the executable fails to start after maximum retries.</exception>
        /// 
        public static Process RunExecutable(string exePath, params string[] arguments)
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Executable not found.", exePath);

            const int maxRetries = 3;                     // Maximum number of launch attempts
            const int initializationTimeoutMs = 1000;     // Time to wait for MainWindowHandle (in milliseconds)
            const int postInitRunDurationMs = 100;       // Time to ensure process remains running after initialization
            const int checkIntervalMs = 100;              // Interval between checks (in milliseconds)

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
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
                        startInfo.ArgumentList.Add(argument);

                    // Initialize the process
                    Process process = new Process { StartInfo = startInfo };

                    // Start the process
                    if (!process.Start())
                    {
                        throw new Exception("Failed to start the process.");
                    }

                    // Set the process priority (Caution: RealTime can affect system stability)
                    try
                    {
                        process.PriorityClass = ProcessPriorityClass.High;
                    }
                    catch (Exception priorityEx)
                    {
                        Console.WriteLine($"Attempt {attempt}: Failed to set process priority. Exception: {priorityEx.Message}");
                        // Optionally, proceed without setting priority or choose a different priority level
                    }

                    Console.WriteLine($"Attempt {attempt}: Process started with PID {process.Id}. Waiting for initialization...");

                    // Record the start time for initialization
                    DateTime initStartTime = DateTime.Now;

                    // Wait until the main window handle is set or until the initialization timeout
                    while ((DateTime.Now - initStartTime).TotalMilliseconds < initializationTimeoutMs)
                    {
                        process.Refresh(); // Refresh process information

                        if (process.HasExited)
                        {
                            throw new Exception("Process exited prematurely during initialization.");
                        }

                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            Console.WriteLine($"Attempt {attempt}: Main window handle detected. Verifying process stability...");
                            break;
                        }

                        Thread.Sleep(checkIntervalMs); // Wait before the next check
                    }

                    // Check if MainWindowHandle was set within the timeout
                    if (process.MainWindowHandle == IntPtr.Zero)
                    {
                        throw new Exception("Initialization timeout: MainWindowHandle was not set.");
                    }

                    // Record the time after initialization
                    DateTime postInitStartTime = DateTime.Now;

                    // Wait for the specified duration to ensure the process remains running
                    while ((DateTime.Now - postInitStartTime).TotalMilliseconds < postInitRunDurationMs)
                    {
                        process.Refresh();

                        if (process.HasExited)
                        {
                            throw new Exception("Process terminated unexpectedly shortly after initialization.");
                        }

                        Thread.Sleep(checkIntervalMs); // Wait before the next check
                    }

                    // At this point, the process has been running successfully for the required duration
                    Console.WriteLine($"Attempt {attempt}: Process is stable and running.");
                    return process; // Successfully launched and stable
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Attempt {attempt}: Failed to launch executable. Exception: {ex.Message}");

                    // Optionally, log the exception details to a log file or monitoring system

                    // If the process was started but failed to stabilize, attempt to kill it
                    try
                    {
                        // Attempt to retrieve the process by name or ID if necessary
                        // For simplicity, assuming 'process' variable is accessible here
                        // If not, consider restructuring the code to keep track of the process
                    }
                    catch
                    {
                        // Ignore any exceptions while trying to kill the process
                    }

                    // Wait before retrying
                    if (attempt < maxRetries)
                    {
                        Console.WriteLine($"Attempt {attempt}: Retrying in {checkIntervalMs}ms...");
                        Thread.Sleep(checkIntervalMs);
                    }
                    else
                    {
                        Console.WriteLine($"Attempt {attempt}: Maximum retries reached. Launching executable failed.");
                        throw new Exception($"Failed to launch the executable '{exePath}' after {maxRetries} attempts.");
                    }
                }
            }

            // This point should not be reached due to the throw in the catch block after max retries
            throw new Exception("Unexpected error in RunExecutable method.");
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

