using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace StressServer
{
    public class ExecutableManager
    {
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
        /// Executes the extracted executable.
        /// </summary>
        /// <param name="exePath">The full path to the executable.</param>
        /// <param name="arguments">Optional arguments to pass to the executable.</param>
        public static Process RunExecutable(string exePath, string arguments = "")
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Executable not found.", exePath);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            Process process = new Process { StartInfo = startInfo };         
            process.Start();

            // wait until it's main window is showing
            while (process.MainWindowHandle == IntPtr.Zero)
            {
                // Refresh process property values
                process.Refresh();

                // Wait a bit before checking again
                Thread.Sleep(100);
            }

            return process;
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

