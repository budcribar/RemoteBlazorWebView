using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Management;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using System.Security.AccessControl;
using System.Net;
using PeakSWC.RemoteWebView;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.IO.Compression;

namespace FileSyncServer.Tests
{
    public static partial class Utilities
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
            using var query = new ManagementObjectSearcher(
                $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");
            foreach (ManagementObject mo in query.Get().Cast<ManagementObject>())
            {
                return Convert.ToInt32(mo["ParentProcessId"]);
            }
            return 0;
        }

        /// <summary>
        /// Kill() returns before the process releases its socket. A health check issued in that
        /// window can succeed against the server that is exiting, and the next navigation then
        /// fails with ERR_CONNECTION_REFUSED. Wait until the process, and then the port, are gone.
        /// </summary>
        private static void StopNamedProcesses(string processName)
        {
            foreach (var existing in Process.GetProcessesByName(processName))
            {
                try
                {
                    existing.Kill(entireProcessTree: true);
                    if (!existing.WaitForExit(10000))
                        Console.WriteLine($"Timed out waiting for {processName} pid {existing.Id} to exit.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error stopping {processName} pid {existing.Id}: {ex.Message}");
                }
            }
        }

        private static void WaitUntilServerPortClosed()
        {
            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (int i = 0; i < 50; i++)
            {
                try
                {
                    using var resp = httpClient.GetAsync("https://localhost:5001/health").GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode)
                        return;
                }
                catch
                {
                    return;
                }
                Thread.Sleep(100);
            }
        }

        private static void WaitForServerHealth(Process process)
        {
            using var httpClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            for (int i = 0; i < 200; i++)
            {
                try
                {
                    if (process.HasExited)
                        throw new InvalidOperationException($"RemoteWebViewService exited with code {process.ExitCode} before https://localhost:5001/health responded.");
                }
                catch (InvalidOperationException ex) when (ex.Message.StartsWith("RemoteWebViewService exited", StringComparison.Ordinal))
                {
                    throw;
                }
                catch
                {
                    // UseShellExecute can refuse HasExited until the process handle is available.
                }

                try
                {
                    using var resp = httpClient.GetAsync("https://localhost:5001/health").GetAwaiter().GetResult();
                    if (resp.IsSuccessStatusCode)
                        return;
                }
                catch { }
                Thread.Sleep(100);
            }

            throw new TimeoutException("RemoteWebViewService did not answer https://localhost:5001/health within 20s after the previous server exited.");
        }

        public static Process StartCsharpServer()
        {
            Stopwatch sw = new();
            sw.Start();
#if !PROFILED_SERVER
            StopNamedProcesses("RemoteWebViewService");
            WaitUntilServerPortClosed();
#endif
#if DEBUG_SERVER
            var relative = @"RemoteBlazorWebView\src\RemoteWebViewService\bin\x64\Debug\net11.0";
#else
            var relative = @"RemoteBlazorWebView\src\RemoteWebViewService\bin\publishNoAuth";
#endif
            var executable = @"RemoteWebViewService.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, relative, executable);

            Process process = new();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), relative);
            process.StartInfo.UseShellExecute = true;
#if !PROFILED_SERVER
            process.Start();
            WaitForServerHealth(process);
#endif
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

            var project = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), RelativeRoot, @"RemoteBlazorWebView\src\RemoteWebViewService\RemoteWebViewService.csproj"));

            Process process = new();
            process.StartInfo.FileName = "dotnet";
            process.StartInfo.Arguments = $"run --project \"{project}\"";
            process.StartInfo.UseShellExecute = false;

            process.Start();
            Console.WriteLine($"Started server in {sw.Elapsed}");
            return process;
        }

        #endregion

        #region WinForm
        public static Process StartRemoteBlazorWinFormsDebugApp(string url, string id) => StartProcess(BlazorWinFormsDebugAppExe(), BlazorWinFormsPath(), url, id);

        public static Process StartRemoteBlazorWinFormsApp(string url, string id) => StartProcess(BlazorWinFormsAppExe(), BlazorWinFormsPath(), url, id);

        public static Process StartRemoteEmbeddedBlazorWinFormsApp(string url, string id) => StartProcess(BlazorWinFormsEmbeddedAppExe(), BlazorWinFormsEmbeddedPath(), url, id);

        public static string BlazorWinFormsDebugPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp";
            var exePath = @"bin\x64\debug\net11.0-windows";
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

        public static Process StartRemoteBlazorWpfDebugApp(string url, string id) => StartProcess(BlazorWpfDebugAppExe(), BlazorWpfDebugPath(), url, id);
        public static string BlazorWpfDebugAppExe() => Path.Combine(BlazorWpfDebugPath(), "RemoteBlazorWebViewTutorial.WpfApp.exe");

        public static string BlazorWpfDebugPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp";
            var exePath = @"bin\x64\debug\net11.0-windows";
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

        public static Process StartRemoteBlazorWpfEmbeddedApp(string url, string id) => StartProcess(BlazorWpfAppEmbeddedExe(), BlazorWpfEmbeddedPath(), url, id);

        public static Process StartRemoteBlazorWpfApp(string url, string id) => StartProcess(BlazorWpfAppExe(), BlazorWpfPath(), url, id);

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
                    return @"..\..\..\..\..\..\..\..\"; // visual studio
                else
                    return @"..\..\..\..\..\..\..\"; // // powershell
            }
        }

        #region WebView
        public static string BlazorWebViewDebugPath()
        {
            var relative = @"RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial";
            var exePath = @"bin\x64\debug\net11.0-windows";
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

        public static Process StartRemoteBlazorWebViewApp(string url, string pid) => StartProcess(BlazorWebViewAppExe(), BlazorWebViewPath(), url, pid);
        public static Process StartRemoteBlazorWebViewEmbeddedApp(string url, string pid) => StartProcess(BlazorWebViewAppEmbeddedExe(), BlazorWebViewEmbeddedPath(), url, pid);
        public static void KillRemoteBlazorWebViewApp() => Kill("RemoteBlazorWebViewTutorial");

        #endregion

        #region Common

        public static string JavascriptFile => Path.Combine(RelativeRoot, @"RemoteBlazorWebView\src\RemoteWebView.Blazor.JS\dist\remote.blazor.desktop.js");

        public static string BASEURL { get => base_URL; set => base_URL = value; }

        public static Process StartProcess(string executable, string directory, string url, string id)
        {
            var sw = Stopwatch.StartNew();
            var fullPath = Path.GetFullPath(executable);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Application executable not found at path: {fullPath}", fullPath);
            }

            Process p = new();
            p.StartInfo.FileName = fullPath;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.Arguments = $"-u={url} -i={id}";
            p.StartInfo.WorkingDirectory = directory;
            var stderr = new StringBuilder();
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    stderr.AppendLine(e.Data);
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            // Touching the process before it has a window raises COMException 0x8007139F.
            // A hosted runner can leave MainWindowHandle at zero for the life of the process.
            var deadline = TimeSpan.FromSeconds(30);
            while (p.MainWindowHandle == IntPtr.Zero)
            {
                p.Refresh();
                if (p.HasExited)
                {
                    try { p.WaitForExit(); } catch (Exception) { }
                    var detail = stderr.ToString().Trim();
                    if (detail.Length > 1800)
                        detail = detail[^1800..];
                    throw new InvalidOperationException(
                        $"{Path.GetFileName(fullPath)} exited with code {p.ExitCode} before creating a window. {detail}");
                }

                if (sw.Elapsed > deadline)
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to stop {Path.GetFileName(fullPath)} after it created no window: {ex.Message}");
                    }

                    throw new TimeoutException(
                        $"{Path.GetFileName(fullPath)} did not create a main window within {deadline.TotalSeconds:0}s.");
                }

                Thread.Sleep(100);
            }

            Console.WriteLine($"{Path.GetFileName(executable)} started in {sw.Elapsed}");

            return p;
        }

        public static int Count(string name) => Process.GetProcesses().Where(p => p.ProcessName == name).Count();
        public static void Kill(string name)
        {
            var processes = Process.GetProcessesByName(name).ToList();

            foreach (var process in processes)
            {
                try
                {
                    // Attempt to kill the process
                    process.Kill();

                    // Wait for the process to exit
                    process.WaitForExit();
                }

                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error occurred while killing process {process.ProcessName} (ID: {process.Id}): {ex.Message}");
                }
            }
        }


        #endregion

        #region Javascript

        public static void DeleteGeneratedJsFiles(string directoryPath)
        {
            try
            {
                string[] jsFiles = Directory.GetFiles(directoryPath, "*.js");
                Regex filePattern = ScriptFilePattern();

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

            StringBuilder indexFileContent = new(File.Exists(indexPath) ? File.ReadAllText(indexPath) : "<html><head></head><body></body></html>");

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

        [GeneratedRegex(@"^script\d+\.js$", RegexOptions.IgnoreCase)]
        private static partial Regex ScriptFilePattern();

        public static string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            Random random = new();
            return new string([.. Enumerable.Repeat(chars, length).Select(s => s[random.Next(s.Length)])]);
        }

        static string CalculateChecksum(string input)
        {
            byte[] data = SHA256.HashData(Encoding.UTF8.GetBytes(input));
            StringBuilder sBuilder = new();
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

        public static void TryVariousPorts()
        {
            AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", true);

            using var handler = new SocketsHttpHandler
            {
                EnableMultipleHttp2Connections = true,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true // Only for testing!
                }
            };
            // using var client = new HttpClient();
            using var client = new HttpClient(handler);
            client.DefaultRequestVersion = HttpVersion.Version30;
            client.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;

            string[] urls =
            [
            "https://localhost:5001",
            "https://localhost:5002",
            "https://localhost:5003", // New HTTP/3-only endpoint
            "https://127.0.0.1:5001",
            "https://127.0.0.1:5002",
            "https://127.0.0.1:5003"  // New HTTP/3-only endpoint
            ];

            foreach (var url in urls)
            {
                try
                {
                    Console.WriteLine($"Attempting to connect to {url}");
                    var response = client.GetAsync(url).Result;
                    Console.WriteLine($"Connected to {url}");
                    Console.WriteLine($"Status: {response.StatusCode}");
                    Console.WriteLine($"Protocol: {response.Version}");


                    if (response.Headers.TryGetValues("alt-svc", out var altSvcValues))
                    {
                        Console.WriteLine($"Alt-Svc header: {string.Join(", ", altSvcValues)}");
                    }
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine($"Error connecting to {url}: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unexpected error connecting to {url}: {ex.Message}");
                }
                Console.WriteLine();
            }

        }
        public static async Task PollHttpRequest(HttpClient httpClient, string url)
        {
            bool serverStarted = false;
            while (!serverStarted)
            {
                try
                {
                    var response = await httpClient.GetAsync(url);
                    serverStarted = response.IsSuccessStatusCode;
                }
                catch
                {
                    // Server is not ready yet, wait a bit before retrying
                    await Task.Delay(10);
                }
            }
        }

        public static string OpenUrlInBrowserWithDevTools(string url)
        {
            // The previous launch swallowed every failure and started a fixed Chrome path with
            // ShellExecute. Connected never fired on the runner, while TestConnectedEvent reached
            // Connected through the default browser. Start a real process, give it a fresh profile
            // so an existing Chrome does not drop the URL, and accept the runner's localhost cert.
            // Edge is the fallback when chrome.exe is not there.
            string[] candidates =
            [
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            ];
            var browser = candidates.FirstOrDefault(File.Exists);
            if (browser == null)
            {
                OpenUrlInBrowser(url);
                var missing = $"ReadyToConnect ran. No Chrome or Edge executable found; opened {url} with the default browser.";
                Console.WriteLine(missing);
                return missing;
            }

            var profile = Path.Combine(Path.GetTempPath(), "rbw-devtools-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = browser,
                    Arguments = $"--new-window --user-data-dir=\"{profile}\" --ignore-certificate-errors --allow-insecure-localhost --auto-open-devtools-for-tabs \"{url}\"",
                    UseShellExecute = false
                });
                var opened = $"ReadyToConnect ran. Opened {url} in {browser}.";
                Console.WriteLine(opened);
                return opened;
            }
            catch (Exception ex)
            {
                OpenUrlInBrowser(url);
                var failed = $"ReadyToConnect ran. {browser} failed ({ex.Message}); opened {url} with the default browser.";
                Console.WriteLine(failed);
                return failed;
            }
        }

        #endregion

        public static async Task<List<string>> GetIds()
        {
            var httpHandler = new HttpClientHandler();

            // Create the gRPC channel with the custom handler
            using var channel = GrpcChannel.ForAddress(BASEURL, new GrpcChannelOptions { HttpHandler = httpHandler });
            var client = new WebViewIPC.WebViewIPCClient(channel);
            var ids = await client.GetIdsAsync(new Empty());
            return ids.Responses.ToList();
        }

        public static async Task SetServerCache(bool isEnabled)
        {
            var httpHandler = new HttpClientHandler();

            // Create the gRPC channel with the custom handler
            using var channel = GrpcChannel.ForAddress(BASEURL, new GrpcChannelOptions { HttpHandler = httpHandler });

            // Create the WebViewIPC client
            var grpcClient = new ClientIPC.ClientIPCClient(channel);


            var cacheRequest = new CacheRequest
            {
                EnableServerCache = isEnabled
            };

            await grpcClient.SetCacheAsync(cacheRequest);

        }

        public static async Task SetClientCache(bool isEnabled)
        {
            var httpHandler = new HttpClientHandler();

            // Create the gRPC channel with the custom handler
            using var channel = GrpcChannel.ForAddress(BASEURL, new GrpcChannelOptions { HttpHandler = httpHandler });

            // Create the WebViewIPC client
            var grpcClient = new ClientIPC.ClientIPCClient(channel);


            var cacheRequest = new CacheRequest
            {
                EnableClientCache = isEnabled
            };

            await grpcClient.SetCacheAsync(cacheRequest);

        }

        public static async Task<bool> GetServerCache()
        {
            var httpHandler = new HttpClientHandler();

            // Create the gRPC channel with the custom handler
            using var channel = GrpcChannel.ForAddress(BASEURL, new GrpcChannelOptions { HttpHandler = httpHandler });

            // Create the WebViewIPC client
            var grpcClient = new ClientIPC.ClientIPCClient(channel);

            var response = await grpcClient.GetServerStatusAsync(new Empty());
            return response.ServerCacheEnabled;
        }

        public static async Task<bool> GetClientCache()
        {
            var httpHandler = new HttpClientHandler();

            // Create the gRPC channel with the custom handler
            using var channel = GrpcChannel.ForAddress(BASEURL, new GrpcChannelOptions { HttpHandler = httpHandler });

            // Create the WebViewIPC client
            var grpcClient = new ClientIPC.ClientIPCClient(channel);

            var response = await grpcClient.GetServerStatusAsync(new Empty());
            return response.ClientCacheEnabled;
        }
        private static string base_URL = "https://localhost:5001";

        public static async Task ShutdownAsync(string id, string url = "https://localhost:5001")
        {
            var httpHandler = new HttpClientHandler();

            // Create the gRPC channel with the custom handler
            using var channel = GrpcChannel.ForAddress(url, new GrpcChannelOptions { HttpHandler = httpHandler });

            // Create the WebViewIPC client
            var client = new WebViewIPC.WebViewIPCClient(channel);

            var response = await client.ShutdownAsync(new IdMessageRequest { Id = id });
        }



        public static HttpClient Client(HttpMessageHandler? handler = null)
        {
            // Instantiate HttpClient with the provided handler or default handler
            var client = handler != null ? new HttpClient(handler) : new HttpClient();

            // Set the base address
            client.BaseAddress = new Uri(BASEURL);

            // Set the timeout
            client.Timeout = TimeSpan.FromMinutes(5); // Adjust as necessary

            // Set the default HTTP version
            client.DefaultRequestVersion = HttpVersion.Version11;

            return client;
        }

        /// <summary>
        /// Extracts all embedded resources from the 'resources' directory and copies them to the execution directory.
        /// </summary>
        public static void ExtractResourcesToExecutionDirectory()
        {
            try
            {
                // Get the executing assembly
                Assembly assembly = Assembly.GetExecutingAssembly();

                // Get the base directory where the executable is running
                string executionDirectory = AppContext.BaseDirectory;

                // Get all embedded resource names
                string[] resourceNames = assembly.GetManifestResourceNames();

                // Define the prefix to identify resources in the 'resources' directory
                // Replace 'YourDefaultNamespace' with your project's default namespace
                string resourcePrefix = "StressServer.resources.";

                foreach (string resourceName in resourceNames)
                {
                    // Check if the resource is within the 'resources' directory
                    if (resourceName.StartsWith(resourcePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        // Determine the relative path by removing the prefix
                        string relativePath = resourceName[resourcePrefix.Length..];

                        // Handle file names with multiple dots
                        // For example, 'subfolder.config.json' should map to 'subfolder\config.json'
                        // Split the relative path into segments based on dots
                        string[] segments = relativePath.Split('.');
                        if (segments.Length < 2)
                        {
                            // Not enough segments to form a valid path, skip this resource
                            Console.WriteLine($"Invalid resource format: {resourceName}");
                            continue;
                        }

                        // Reconstruct the file path
                        // Assume the last segment is the file extension
                        string fileExtension = segments[^1];
                        string fileName = segments[^2] + "." + segments[^1];
                        string[] directorySegments = new string[segments.Length - 2];
                        Array.Copy(segments, 0, directorySegments, 0, segments.Length - 2);
                        string directoryPath = Path.Combine(directorySegments);

                        // Combine to form the full relative path
                        string combinedRelativePath = Path.Combine(directoryPath, fileName);

                        // Determine the destination path in the execution directory
                        string destinationPath = Path.Combine(executionDirectory, combinedRelativePath).Replace("_", "-");

                        // Ensure the destination directory exists
                        string destinationDirectory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
                        if (!Directory.Exists(destinationDirectory))
                        {
                            Directory.CreateDirectory(destinationDirectory);
                            Console.WriteLine($"Created directory: {destinationDirectory}");
                        }

                        // Extract and write the resource to the destination path
                        using Stream? resourceStream = assembly.GetManifestResourceStream(resourceName);
                        if (resourceStream == null)
                        {
                            Console.WriteLine($"Failed to load resource: {resourceName}");
                            continue;
                        }

                        using FileStream fileStream = new(destinationPath, FileMode.Create, FileAccess.Write);
                        resourceStream.CopyTo(fileStream);
                        Console.WriteLine($"Extracted resource: {resourceName} to {destinationPath}");
                    }
                }

                Console.WriteLine("All embedded resources have been extracted successfully.");

                ZipFile.ExtractToDirectory("playwright.zip", executionDirectory);
               
                Console.WriteLine("Extracted playwright files");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while extracting resources: {ex.Message}");
            }
        }

        public static void KillExistingProcesses(string processName)
        {

            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    Console.WriteLine($"Killing process: {process.ProcessName} (ID: {process.Id})");
                    process.Kill();
                    process.WaitForExit(); // Optionally wait for the process to exit
                }
                catch (Exception ex)
                {

                    Console.WriteLine($"Error killing process: {ex.Message}");
                }

            }

        }

        /// <summary>
        /// Modifies the Read and Delete permissions for a specified user on a given file.
        /// </summary>
        /// <param name="filePath">The path to the file.</param>
        /// <param name="user">The user account (e.g., "DOMAIN\\Username").</param>
        /// <param name="grantRead">True to grant Read and Delete permissions; False to remove them.</param>
        /// <param name="disableInheritance">Optional. True to disable inheritance on the file; False to leave it as is.</param>
        public static void ModifyFilePermissions(string filePath, string user, bool grantRead, bool disableInheritance = true)
        {
            // Validate input parameters
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            if (string.IsNullOrWhiteSpace(user))
                throw new ArgumentException("User cannot be null or empty.", nameof(user));

            FileInfo fileInfo = new(filePath);

            if (!fileInfo.Exists)
                throw new FileNotFoundException("The specified file does not exist.", filePath);

            try
            {
                // Get the current ACL (Access Control List) of the file
                FileSecurity fileSecurity = fileInfo.GetAccessControl();

                if (grantRead)
                {
                    // Define the access rule to grant Read and Delete permissions
                    FileSystemAccessRule allowReadDeleteRule = new(
                        user,
                        FileSystemRights.Read | FileSystemRights.Delete,
                        InheritanceFlags.None,
                        PropagationFlags.NoPropagateInherit,
                        AccessControlType.Allow);

                    // Check if the rule already exists to prevent duplicates
                    bool ruleExists = false;
                    foreach (FileSystemAccessRule rule in fileSecurity.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount)))
                    {
                        if (rule.IdentityReference.Value.Equals(user, StringComparison.OrdinalIgnoreCase) &&
                            rule.FileSystemRights.HasFlag(FileSystemRights.Read) &&
                            rule.FileSystemRights.HasFlag(FileSystemRights.Delete) &&
                            rule.AccessControlType == AccessControlType.Allow)
                        {
                            ruleExists = true;
                            break;
                        }
                    }

                    if (!ruleExists)
                    {
                        // Add the access rule since it doesn't exist
                        fileSecurity.AddAccessRule(allowReadDeleteRule);
                        Console.WriteLine($"Granted Read and Delete permissions to {user}.");
                    }
                    else
                    {
                        Console.WriteLine($"Read and Delete permissions for {user} are already granted.");
                    }
                }
                else
                {
                    // Define the access rule to remove Read and Delete permissions
                    FileSystemAccessRule allowReadDeleteRule = new(
                        user,
                        FileSystemRights.Read | FileSystemRights.Delete,
                        InheritanceFlags.None,
                        PropagationFlags.NoPropagateInherit,
                        AccessControlType.Allow);

                    // Remove all matching Allow Read and Delete rules for the user
                    fileSecurity.RemoveAccessRuleAll(allowReadDeleteRule);

                }

                // Optionally, handle inheritance
                if (disableInheritance)
                {
                    bool isInheritanceEnabled = !fileSecurity.AreAccessRulesProtected;

                    if (isInheritanceEnabled)
                    {
                        // Disable inheritance and remove inherited rules
                        fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                        Console.WriteLine("Inheritance disabled and inherited rules removed.");
                    }
                    else
                    {
                        Console.WriteLine("Inheritance is already disabled.");
                    }
                }

                // Apply the updated ACL to the file
                fileInfo.SetAccessControl(fileSecurity);
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied: {ex.Message}");
                // Handle according to your application's requirements
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while modifying file permissions: {ex.Message}");
                // Handle according to your application's requirements
                throw;
            }
        }


        //private static readonly char[] chars =
        //   "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();

        //   public static string GenerateRandomString(int length)
        //   {
        //       StringBuilder result = new StringBuilder(length);
        //       Random random = new Random();

        //       for (int i = 0; i < length; i++)
        //       {
        //           result.Append(chars[random.Next(chars.Length)]);
        //       }

        //       return result.ToString();
        //   }

        public static X509Certificate2 LoadCerCertificate(string cerFilePath)
        {
            return X509CertificateLoader.LoadCertificateFromFile(cerFilePath);
        }
        public static void AddCertificateToLocalMachine(string cerFilePath)
        {
            try
            {
                // Load the certificate
                X509Certificate2 certificate = LoadCerCertificate(cerFilePath);

                // Check if the certificate has expired
                DateTime now = DateTime.Now;
                if (now < certificate.NotBefore || now > certificate.NotAfter)
                {
                    Console.WriteLine("Error: Certificate is either not yet valid or has expired.");
                    return;
                }

                // Open the Local Machine's Trusted Root store
                using X509Store store = new(StoreName.Root, StoreLocation.LocalMachine);
                store.Open(OpenFlags.ReadWrite);

                // Check if the certificate already exists
                bool exists = false;
                foreach (var cert in store.Certificates)
                {
                    if (cert.Thumbprint.Equals(certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    store.Add(certificate);
                    Console.WriteLine("Certificate added to Local Machine's Trusted Root store.");
                }
                else
                {
                    Console.WriteLine("Certificate already exists in Local Machine's Trusted Root store.");
                }

                store.Close();
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine("Error: Access denied. Please run the application with the necessary permissions.");
            }
            catch (CryptographicException ex)
            {
                Console.WriteLine($"Cryptographic error: {ex.Message}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"IO error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An unexpected error occurred: {ex.Message}");
            }
        }

        public static async Task KillProcessesAsync(string processName)
        {
            var existingProcesses = Process.GetProcessesByName(processName);

            if (existingProcesses.Length == 0)
            {
                Console.WriteLine($"No running processes found with name: {processName}");
                return;
            }

            Console.WriteLine($"Killing {existingProcesses.Length} instance(s) of process: {processName}");

            List<Task> killTasks = existingProcesses.Select(async process =>
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                        Console.WriteLine($"Sent kill signal to process ID: {process.Id}");

                        // Wait for the process to exit with a timeout (e.g., 5 seconds)
                        await process.WaitForExitAsync();

                        if (process.HasExited)
                        {
                            Console.WriteLine($"Process ID: {process.Id} has exited.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to kill process {process.ProcessName} (ID: {process.Id}): {ex.Message}");
                }
            }).ToList();

            await Task.WhenAll(killTasks);
        }

        public static void CopyDirectory(string sourceDir, string destinationDir)
        {
            // Get the subdirectories for the specified directory.
            DirectoryInfo dir = new(sourceDir);

            if (!dir.Exists)
            {
                throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDir}");
            }

            // Create the destination directory if it doesn't exist.
            if (!Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            // Copy all the files to the destination directory.
            FileInfo[] files = dir.GetFiles();
            foreach (FileInfo file in files)
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true); // Overwrite if exists
            }

            // Copy all subdirectories and their files recursively.
            DirectoryInfo[] subdirectories = dir.GetDirectories();
            foreach (DirectoryInfo subdir in subdirectories)
            {
                string newDestinationDir = Path.Combine(destinationDir, subdir.Name);
                CopyDirectory(subdir.FullName, newDestinationDir);
            }
        }
    }
}
