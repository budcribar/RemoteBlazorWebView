using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Google.Protobuf.WellKnownTypes;
using PeakSWC.RemoteWebView;
using Grpc.Core;

namespace ServerStartupTimer
{
    class Program
    {
        public static long GetFileSize(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("File not found.", filePath);
            }

            FileInfo fileInfo = new FileInfo(filePath);
            return fileInfo.Length;
        }

        public static async void TestCreateWebView()
        {
            string grpcUrl = @"https://localhost:5001/";
            WebViewIPC.WebViewIPCClient? client;
            var channel = GrpcChannel.ForAddress(grpcUrl);
            client = new WebViewIPC.WebViewIPCClient(channel);

            int loops = 125;
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < loops; i++)
            {
                var response = client.CreateWebView(new CreateWebViewRequest { Id = i.ToString() });

                await foreach (var message in response.ResponseStream.ReadAllAsync())
                {
                    if (message.Response == "created:")
                        break;
                    Console.WriteLine(message.Response);
                }

                //response.Dispose();
                //var reader = client.FileReader();
                //reader.RequestStream.WriteAsync(new FileReadRequest { })
            }
            stopwatch.Stop();
            Console.WriteLine($"Avg Time for {loops} CreateWebViewRequest request: {stopwatch.ElapsedMilliseconds / loops} ms per request");

            var ids = await client.GetIdsAsync(new Empty());
            Debug.Assert(ids.Responses.Count == loops);

            stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < loops; i++)
            {
                client.Shutdown(new IdMessageRequest { Id = i.ToString() });
                Console.WriteLine(i);
            }
            Console.WriteLine($"Avg Time for {loops} Shutdown request: {stopwatch.ElapsedMilliseconds / loops} ms per request");
            ids = await client.GetIdsAsync(new Empty());
            Debug.Assert(ids.Responses.Count == 0);
        }
        public static async void TestClientIPCService()
        {
            int loops = 125; // hangs after 127
            string grpcUrl = @"https://localhost:5001/";
            ClientIPC.ClientIPCClient? client;
            var httpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWebText, new HttpClientHandler());
            var channel = GrpcChannel.ForAddress(grpcUrl, new GrpcChannelOptions { HttpHandler = httpHandler });
            client = new ClientIPC.ClientIPCClient(channel);
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < loops; i++)
            {
                var response = await client!.GetServerStatusAsync(new Empty { });
                //Console.WriteLine(i);
            }
            stopwatch.Stop();
            Console.WriteLine($"Avg Time for {loops} GetServerStatusAsync request: {stopwatch.ElapsedMilliseconds/loops} ms per request");
        }

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
                FileName = @"..\..\..\..\..\..\RemoteWebViewService\bin\publishNoAuth\RemoteWebViewService.exe",
                RedirectStandardOutput = true
            };

            Console.WriteLine($"Server File Size {GetFileSize(processStartInfo.FileName)}");

            using (var process = Process.Start(processStartInfo))
            {
                var stopwatch = Stopwatch.StartNew();
                string serverHost = "localhost";
                int serverPort = 5001; // Change this to your server's port

                // Poll the port
                //await PollPort(serverHost, serverPort);

                string url = $"https://{serverHost}:{serverPort}";
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

                    stopwatch.Restart();
                    for (int i = 0; i < 500; i++)
                    {
                        var res = await httpClient.GetAsync(url);
                        //Console.WriteLine(i);
                    }
                    stopwatch.Stop();
                    Console.WriteLine($"Time for 500 requests: {stopwatch.ElapsedMilliseconds} ms");
                    TestCreateWebView();
                   // TestClientIPCService();

                }
                Console.ReadKey();
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
