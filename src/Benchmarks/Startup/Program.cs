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
using System.Collections.Concurrent;

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

        public static void TestCreateWebView(int numBuffers, int minSize, int maxSize)
        {
            List<Process> processList = new List<Process>();

            for (int i = 0; i < numBuffers; i++)
            {

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = @"..\..\..\..\..\StressClient\publish\StressClient.exe",
                    Arguments = $"{numBuffers} {minSize} {maxSize}",
                    RedirectStandardOutput = true
                };
                var process = Process.Start(processStartInfo);
                if (process != null)
                    processList.Add(process);
                Task.Delay(100).Wait();
            }
            int count = 0;
            foreach (var process in processList)
            {
                if (!process?.WaitForExit(30000) ?? true)
                {
                    Console.WriteLine($"Process {count++} timed out");
                }
            }
            
        }

        public static async Task TestCreateWebView(int numLoops)
        {
            var disposables = new ConcurrentBag<IDisposable>();
            try
            {
               

                var stopwatch = new Stopwatch();
                var createdCount = 0;
                var shutdownCount = 0;
                var tasks = new List<Task>();
                var responseStreams = new ConcurrentDictionary<string, (WebViewIPC.WebViewIPCClient, IAsyncStreamReader<WebMessageResponse>)>();

                stopwatch.Start();
                for (int i = 1; i <= numLoops; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var handler = new SocketsHttpHandler
                        {
                            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                            KeepAlivePingDelay = TimeSpan.FromSeconds(90),
                            KeepAlivePingTimeout = TimeSpan.FromSeconds(60),
                            EnableMultipleHttp2Connections = true
                        };
                        disposables.Add(handler);
                        var httpClient = new HttpClient(handler);
                        disposables.Add(httpClient);
                        var channel = GrpcChannel.ForAddress("https://localhost:5001", new GrpcChannelOptions
                        {
                            HttpClient = httpClient
                        });
                        disposables.Add(channel);
                        var client = new WebViewIPC.WebViewIPCClient(channel);
                        string id = Guid.NewGuid().ToString();
                        var response = client.CreateWebView(new CreateWebViewRequest { Id = id });
                        responseStreams[id] = (client, response.ResponseStream);
                        await foreach (var message in response.ResponseStream.ReadAllAsync())
                        {
                            if (message.Response == "created:")
                            {
                                Interlocked.Increment(ref createdCount);
                                break;
                            }
                            Console.WriteLine($"Creation message for {id}: {message.Response}");
                        }
                    }));
                }
                await Task.WhenAll(tasks);
                stopwatch.Stop();
                Console.WriteLine($"Avg Time for {numLoops} CreateWebViewRequest request: {stopwatch.ElapsedMilliseconds / (double)numLoops} ms per request");

                var ids = await responseStreams.First().Value.Item1.GetIdsAsync(new Empty());
                Debug.Assert(ids.Responses.Count == numLoops);

                stopwatch.Reset();
                stopwatch.Start();
                tasks.Clear();

                foreach (var idPair in responseStreams)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await responseStreams[idPair.Key].Item1.ShutdownAsync(new IdMessageRequest { Id = idPair.Key });
                            await foreach (var message in idPair.Value.Item2.ReadAllAsync())
                            {
                                if (message.Response == "shutdown:")
                                {
                                    Interlocked.Increment(ref shutdownCount);
                                    break;
                                }
                                Console.WriteLine($"Shutdown message for {idPair.Key}: {message.Response}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error on {idPair.Key}: {ex.Message}");
                        }
                    }));
                }
                await Task.WhenAll(tasks);
                stopwatch.Stop();
                Console.WriteLine($"Avg Time for {numLoops} Shutdown request: {stopwatch.ElapsedMilliseconds / (double)numLoops} ms per request");

                //ids = await responseStreams.First().Value.Item1.GetIdsAsync(new Empty());
                //Debug.Assert(ids.Responses.Count == 0);
            }
            finally
            {
                foreach (var disposable in disposables)
                {
                    disposable?.Dispose();
                }
            }
        }

        public static async Task TestCreateWebView()
        {
            //string grpcUrl = @"https://localhost:5001/";
            WebViewIPC.WebViewIPCClient? client;

            var handler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
                EnableMultipleHttp2Connections = true
            };

            var httpClient = new HttpClient(handler);

            var channel = GrpcChannel.ForAddress("https://localhost:5001", new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            client = new WebViewIPC.WebViewIPCClient(channel);

            int loops = 500;
            var stopwatch = Stopwatch.StartNew();
            for (int i = 1; i <= loops; i++)
            {
                var response = client.CreateWebView(new CreateWebViewRequest { Id = i.ToString() });

                if (i%100 == 0) 
                    Console.WriteLine(i);

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
            //client = new WebViewIPC.WebViewIPCClient(channel);
            var ids = await client.GetIdsAsync(new Empty());
            Debug.Assert(ids.Responses.Count == loops);

            stopwatch = Stopwatch.StartNew();
            
            for (int i = 1; i <= loops; i++)
            {
                try
                {
                    var deadline = DateTime.UtcNow.AddSeconds(2);
                    await client.ShutdownAsync(new IdMessageRequest { Id = i.ToString() }, null,deadline);
                    //ait Task.Delay(1000);
                }
                catch (Exception) {
                    Console.WriteLine($"Deadline reached on {i}");
                }

                if (i%100 == 0)
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
         
            using var httpHandler = new GrpcWebHandler(GrpcWebMode.GrpcWebText, new HttpClientHandler());
            using var channel = GrpcChannel.ForAddress(grpcUrl, new GrpcChannelOptions { HttpHandler = httpHandler });
            ClientIPC.ClientIPCClient client = new ClientIPC.ClientIPCClient(channel);
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
            KillExistingProcesses("StressClient");

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
                //using (var httpClient = new HttpClient())
                //{
                //    // Poll for a successful HTTP request
                //    await PollHttpRequest(httpClient, url);
                //    stopwatch.Stop();
                //    Console.WriteLine($"Time to first request: {stopwatch.ElapsedMilliseconds} ms");

                //    stopwatch.Restart();
                //    await PollHttpRequest(httpClient, url);
                //    stopwatch.Stop();
                //    Console.WriteLine($"Time to second request: {stopwatch.ElapsedMilliseconds} ms");
                //}

                //using (var httpClient = new HttpClient())
                //{
                //    // Poll for a successful HTTP request
                //    await PollHttpRequest(httpClient, url);
                //    stopwatch.Stop();
                //    Console.WriteLine($"Time to third request: {stopwatch.ElapsedMilliseconds} ms");

                //    stopwatch.Restart();
                //    await PollHttpRequest(httpClient, url);
                //    stopwatch.Stop();
                //    Console.WriteLine($"Time to fourth request: {stopwatch.ElapsedMilliseconds} ms");

                //    stopwatch.Restart();
                //    for (int i = 0; i < 500; i++)
                //    {
                //        var res = await httpClient.GetAsync(url);
                //        //Console.WriteLine(i);
                //    }
                //    stopwatch.Stop();
                //    Console.WriteLine($"Time for 500 requests: {stopwatch.ElapsedMilliseconds} ms");
                //}

                for (int i = 0;i < 1;i++)
                {
                    Console.WriteLine($"TestCreateWebView loop{i}");
                    //await TestCreateWebView(200);
                    KillExistingProcesses("StressClient");
                    TestCreateWebView(1000,1024,10240);
                }
                      
                // TestClientIPCService();

                
                //Console.ReadKey();
               // process?.Kill(); // Stop the server
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
