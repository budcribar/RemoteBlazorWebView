using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Management;

namespace WebdriverTestProject
{
    public class Utilities
    {
        #region Server
        
        static string GetParentProcessName()
        {
            try
            {
                Process currentProcess = Process.GetCurrentProcess();
                int parentProcessId = GetParentProcessId(currentProcess.Id);

                if (parentProcessId != 0)
                {
                    Process parentProcess = Process.GetProcessById(parentProcessId);
                    return parentProcess.ProcessName;
                }
                else
                {
                    return "Unable to retrieve parent process.";
                }
            }
            catch (Exception ex)
            {
                return $"An error occurred: {ex.Message}";
            }
        }

        static int GetParentProcessId(int processId)
        {
            using (var query = new ManagementObjectSearcher(
                $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}"))
            {
                foreach (ManagementObject mo in query.Get())
                {
                    return Convert.ToInt32(mo["ParentProcessId"]);
                }
            }
            return 0;
        }

        public static Process StartCsharpServer()
        {
            Stopwatch sw = new();
            sw.Start();

            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "RemoteWebViewService")?.Kill();
#if DEBUG_SERVER
            var relative = @"RemoteBlazorWebView\src\RemoteWebViewService\bin\x64\Debug\net9";
#else
            var relative = @"RemoteBlazorWebView\src\RemoteWebViewService\bin\publishNoAuth";
#endif
            var executable = @"RemoteWebViewService.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, executable);

            Process process = new();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), relative);
            process.StartInfo.UseShellExecute = true;

            process.Start();
            Console.WriteLine($"Started server in {sw.Elapsed}");
            return process;
        }

        public static Process StartServer()
        {
            string? envVarValue = Environment.GetEnvironmentVariable(variable: "Rust");
            if (envVarValue != null) return StartRustServer();
            return StartCsharpServer();
        }

        public static Process StartRustServer()
        {

            Stopwatch sw = new();
            sw.Start();

            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "http_to_grpc_bridge")?.Kill();
            var relative = @"http_to_grpc_bridge";
            var executable = @"http_to_grpc_bridge.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, executable);

            Process process = new();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), relative);
            process.StartInfo.UseShellExecute = true;

            process.Start();
            Debug.WriteLine($"Started RUST server in {sw.Elapsed}");
            return process;
        }


        public static Process StartServerFromPackage()
        {

            Stopwatch sw = new();
            sw.Start();

            Process process = new();
            process.StartInfo.FileName = "RemoteWebViewService";
            process.StartInfo.UseShellExecute = true;

            process.Start();
            Console.WriteLine($"Started server in {sw.Elapsed}");
            return process;
        }

#endregion

        #region WinForm
        public static Process StartRemoteBlazorWinFormsDebugApp() => StartProcess(BlazorWinFormsDebugAppExe(), BlazorWinFormsPath());

        public static Process StartRemoteBlazorWinFormsApp() => StartProcess(BlazorWinFormsAppExe(), BlazorWinFormsPath());

        public static Process StartRemoteEmbeddedBlazorWinFormsApp() => StartProcess(BlazorWinFormsEmbeddedAppExe(), BlazorWinFormsEmbeddedPath());

        public static string BlazorWinFormsDebugPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp";
            var exePath = @"bin\x64\debug\net9.0-windows";  
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }
        public static string BlazorWinFormsDebugAppExe() => Path.Combine(BlazorWinFormsDebugPath(), "RemoteBlazorWebViewTutorial.WinFormsApp.exe");

        public static string BlazorWinFormsPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin";
            var exePath = "publish";
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }

        public static string BlazorWinFormsEmbeddedPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin";
            var exePath = "publishEmbedded";
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }

        public static string BlazorWinFormsAppExe() => Path.Combine(BlazorWinFormsPath(), "RemoteBlazorWebViewTutorial.WinFormsApp.exe");

        public static string BlazorWinFormsEmbeddedAppExe() => Path.Combine(BlazorWinFormsEmbeddedPath(), "RemoteBlazorWebViewTutorial.WinFormsApp.exe");

        public static void KillBlazorWinFormsApp()
        {
            Process.GetProcesses().Where(p => p.ProcessName == "RemoteBlazorWebViewTutorial.WinFormsApp").ToList().ForEach(x => x.Kill());
        }
        #endregion

        #region Wpf

        public static Process StartRemoteBlazorWpfDebugApp() => StartProcess(BlazorWpfDebugAppExe(), BlazorWpfPath());
        public static string BlazorWpfDebugAppExe() => Path.Combine(BlazorWpfDebugPath(), "RemoteBlazorWebViewTutorial.WpfApp.exe");

        public static string BlazorWpfDebugPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp";
            var exePath = @"bin\x64\debug\net9.0-windows";
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }

        public static string BlazorWpfAppExe()
        {
            return Path.Combine(BlazorWpfPath(), "RemoteBlazorWebViewTutorial.WpfApp.exe");
        }
        public static string BlazorWpfPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin";
            var exePath = "publish";
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }

        public static string BlazorWpfEmbeddedPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin";
            var exePath = "publishEmbedded";
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }

        public static string BlazorWpfAppEmbeddedExe()
        {
            return Path.Combine(BlazorWpfEmbeddedPath(), "RemoteBlazorWebViewTutorial.WpfApp.exe");
        }

        public static Process  StartRemoteBlazorWpfEmbeddedApp() => StartProcess(BlazorWpfAppEmbeddedExe(), BlazorWpfEmbeddedPath());

        public static Process StartRemoteBlazorWpfApp() => StartProcess(BlazorWpfAppExe(), BlazorWpfPath());

        public static void KillRemoteBlazorWpfApp() => Kill("RemoteBlazorWebViewTutorial.WpfApp");

        public static int CountRemoteBlazorWinFormsApp() => Count("RemoteBlazorWebViewTutorial.WinFormsApp");
        public static int CountRemoteBlazorWpfApp() => Count("RemoteBlazorWebViewTutorial.WpfApp");
        #endregion
        
        
        
        private static string RelativeRoot
        {
            get
            {
                var ppn = GetParentProcessName();
                if (ppn == "vstest.console")
                    return @"..\..\..\..\..\..\..\"; // visual studio
                else
                    return @"..\..\..\..\..\..\"; // // powershell
            }
        }

        #region WebView
        public static string BlazorWebViewDebugPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial";
            var exePath = @"bin\debug\net9.0";
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }

        public static string BlazorWebViewPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin";
            var exePath = @"publish";
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }

        public static string BlazorWebViewDebugAppExe()
        {
            return Path.Combine(BlazorWebViewDebugPath(), "RemoteBlazorWebViewTutorial.exe");
        }

        public static string BlazorWebViewAppExe()
        {
            return Path.Combine(BlazorWebViewPath(), "RemoteBlazorWebViewTutorial.exe");
        }

        public static string BlazorWebViewEmbeddedPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin";
            var exePath = "publishEmbedded";
            return Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, exePath);
        }
      

        public static string BlazorWebViewAppEmbeddedExe()
        {
            return Path.Combine(BlazorWebViewEmbeddedPath(), "RemoteBlazorWebViewTutorial.exe");
        }

        public static Process StartRemoteBlazorWebViewApp() => StartProcess(BlazorWebViewAppExe(), BlazorWebViewPath());
        public static Process StartRemoteBlazorWebViewEmbeddedApp() => StartProcess(BlazorWebViewAppEmbeddedExe(), BlazorWebViewEmbeddedPath());
        public static void KillRemoteBlazorWebViewApp() => Kill("RemoteBlazorWebViewTutorial");

        #endregion

        #region Common

        public static string JavascriptFile = Path.Combine(RelativeRoot, @"RemoteBlazorWebView\src\RemoteWebView.Blazor.JS\dist\remote.blazor.desktop.js");
        public static Process StartProcess(string executable, string directory)
        {
            Stopwatch sw = new();

            Process p = new();
            p.StartInfo.FileName = Path.GetFullPath(executable);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = @"-u=https://localhost:5001";
            p.StartInfo.WorkingDirectory = directory;
            p.Start();


            // Try to prevent COMException 0x8007139F
            while (p.MainWindowHandle == IntPtr.Zero)
            {
                // Refresh process property values
                p.Refresh();

                // Wait a bit before checking again
                Thread.Sleep(100);
            }

            Console.WriteLine($"Clients started in {sw.Elapsed}");

            return p;
        }

        public static int Count(string name) => Process.GetProcesses().Where(p => p.ProcessName == name).Count();
        public static void Kill(string name) => Process.GetProcesses().Where(p => p.ProcessName == name).ToList().ForEach(x =>
        {
            x.Kill();
            // Wait for exit before re-starting
            x.WaitForExit();
        });

        #endregion

        #region Javascript

        public static void DeleteGeneratedJsFiles(string directoryPath)
        {
            try
            {
                string[] jsFiles = Directory.GetFiles(directoryPath, "*.js");
                Regex filePattern = new Regex(@"^script\d+\.js$", RegexOptions.IgnoreCase);

                foreach (string file in jsFiles)
                {
                    string fileName = Path.GetFileName(file);
                    if (filePattern.IsMatch(fileName))
                    {
                        File.Delete(file);
                        Console.WriteLine($"Deleted: {file}");
                    }
                }
                Console.WriteLine("Target JavaScript files have been deleted.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }
        public static void GenJavascript(int numberOfFiles = 10, int stringLength = 1000)
        {
            string basePath = @"wwwroot"; // Set your base path
            string indexPath = Path.Combine(basePath, "index.html");

            StringBuilder indexFileContent = new StringBuilder(File.Exists(indexPath) ? File.ReadAllText(indexPath) : "<html><head></head><body></body></html>");

            for (int i = 1; i <= numberOfFiles; i++)
            {
                string fileName = $"script{i}.js";
                string filePath = Path.Combine(basePath, fileName);
                string randomString = GenerateRandomString(stringLength);
                string checksum = CalculateChecksum(randomString);

                string jsContent = $@"
                (function() {{
                    const string = '{randomString}';
                    const checksum = '{checksum}';
                    async function verifyChecksum() {{
                        const calculatedChecksum = await sha256(string);
                        if(calculatedChecksum !== checksum) {{
                            alert('Checksum verification failed for {fileName}.');
                            throw new Error('Checksum verification failed.');
                        }}
                    }}
                    async function sha256(message) {{
                        const msgBuffer = new TextEncoder().encode(message);
                        const hashBuffer = await crypto.subtle.digest('SHA-256', msgBuffer);
                        const hashArray = Array.from(new Uint8Array(hashBuffer));
                        const hashHex = hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
                        return hashHex;
                    }}
                    verifyChecksum();
                    console.log('{fileName} passed');
                }})();
                ";

                File.WriteAllText(filePath, jsContent);

                // Add script tag for this file to index.html
                int bodyEndIndex = indexFileContent.ToString().LastIndexOf("</body>");
                indexFileContent.Insert(bodyEndIndex, $"<script src=\"{fileName}\"></script>\n");
            }

            File.WriteAllText(indexPath, indexFileContent.ToString());
        }

        static string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            Random random = new Random();
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        static string CalculateChecksum(string input)
        {
            using SHA256 sha256Hash = SHA256.Create();
            byte[] data = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(input));
            StringBuilder sBuilder = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                sBuilder.Append(data[i].ToString("x2"));
            }
            return sBuilder.ToString();
        }
        public static void OpenUrlInBrowser(string url)
        {
            try
            {
                // Use the default browser's executable to open the URL
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true // Important for .NET Core/5+/6+ compatibility
                });
                Console.WriteLine($"Opened {url} in the default browser.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open URL: {ex.Message}");
            }
        }
        public static void OpenUrlInBrowserWithDevTools(string url)
        {
            try
            {
                // Specify the path to the Chrome executable
                string chromePath = @"C:\Program Files\Google\Chrome\Application\chrome.exe";

                // Use the --auto-open-devtools-for-tabs command line switch to open dev tools
                Process.Start(new ProcessStartInfo
                {
                    FileName = chromePath,
                    Arguments = $"--new-window --auto-open-devtools-for-tabs {url}",
                    UseShellExecute = true
                });
                Console.WriteLine($"Opened {url} in Chrome with developer tools.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to open URL in Chrome with developer tools: {ex.Message}");
            }
        }

    }

    #endregion
}
