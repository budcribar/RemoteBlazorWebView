using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace ServerStartupTimer
{
    class Program
    {
        static async Task Main(string[] _)
        {

            static void KillExistingProcesses(string processName)
            {
                try
                {
                    foreach (var process in Process.GetProcessesByName(processName))
                    {
                        Console.WriteLine($"Killing process: {process.ProcessName} (ID: {process.Id})");
                        process.Kill();
                        process.WaitForExit(); // Optionally wait for the process to exit
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error killing process: {ex.Message}");
                }
            }

            KillExistingProcesses("RemoteWebViewService");

            var processStartInfo = new ProcessStartInfo
            {
                FileName = @"..\..\..\..\..\RemoteWebViewService\bin\publishNoAuth\RemoteWebViewService.exe",
                RedirectStandardOutput = true
            };

            using (var process = Process.Start(processStartInfo))
            {
                var stopwatch = Stopwatch.StartNew();
                string serverHost = "localhost";
                int serverPort = 5001; // Change this to your server's port

                // Poll the port
                //await PollPort(serverHost, serverPort);

                string url = $"https://{serverHost}:{serverPort}/test";
                using (var httpClient = new HttpClient())
                {
                    // Poll for a successful HTTP request
                    await PollHttpRequest(httpClient, url);
                    stopwatch.Stop();
                    Console.WriteLine($"Time to first request: {stopwatch.ElapsedMilliseconds} ms");

                    stopwatch.Restart();
                    await PollHttpRequest(httpClient, url);
                    stopwatch.Stop();
                    Console.WriteLine($"Time to second request: {stopwatch.ElapsedMilliseconds} ms");
                }

                using (var httpClient = new HttpClient())
                {
                    // Poll for a successful HTTP request
                    await PollHttpRequest(httpClient, url);
                    stopwatch.Stop();
                    Console.WriteLine($"Time to third request: {stopwatch.ElapsedMilliseconds} ms");

                    stopwatch.Restart();
                    await PollHttpRequest(httpClient, url);
                    stopwatch.Stop();
                    Console.WriteLine($"Time to fourth request: {stopwatch.ElapsedMilliseconds} ms");
                }

                process?.Kill(); // Stop the server
            }
        }

        static async Task PollPort(string host, int port)
        {
            bool portOpen = false;
            while (!portOpen)
            {
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(host, port);
                    portOpen = true;
                }
                catch
                {
                    // Port is not open yet, wait a bit before retrying
                    await Task.Delay(10);
                }
            }
        }

        static async Task PollHttpRequest(HttpClient httpClient, string url)
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
    }
}
