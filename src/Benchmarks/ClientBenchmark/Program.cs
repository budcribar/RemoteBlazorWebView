using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.FileProviders;
using PeakSWC.RemoteWebView;
using System;
using System.Diagnostics;
using System.Text;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using System.Net;

namespace ClientBenchmark
{
   
    public class ClientBenchmarks
    {

        // what happens when you have multiple reads of the same file?
        private string _testGuid;
        private string _testFilePath;
        private string _rootDirectory;
        private string _testFileName = "wwwroot/css/site";
        private WebViewIPC.WebViewIPCClient _client;
        private BrowserIPC.BrowserIPCClient _browser;
        private string randomString;
        private HttpClient httpClient;
        private string URL = "https://localhost:5001";
        private bool _prodServer = false;
        private int fileSize = 102400;
        private int maxFiles = 100;

        private static readonly char[] chars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890".ToCharArray();

        public static string GenerateRandomString(int length)
        {
            StringBuilder result = new StringBuilder(length);
            Random random = new Random();

            for (int i = 0; i < length; i++)
            {
                result.Append(chars[random.Next(chars.Length)]);
            }

            return result.ToString();
        }
        private void KillExistingProcesses(string processName)
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

        private async Task PollHttpRequest(HttpClient httpClient, string url)
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


        [GlobalSetup]
        public void Setup()

        {
            if (_prodServer)
                KillExistingProcesses("RemoteWebViewService");

            var processStartInfo = new ProcessStartInfo
            {
#if DEBUG
                FileName = @"..\..\..\..\..\RemoteWebViewService\bin\publishNoAuth\RemoteWebViewService.exe",
#else
                FileName = @"..\..\..\..\..\..\..\..\..\RemoteWebViewService\bin\publishNoAuth\RemoteWebViewService.exe",
#endif
                RedirectStandardOutput = true
            };

            var handler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(90),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(60),
                MaxConnectionsPerServer = 1000,
                EnableMultipleHttp2Connections = true
            };

            ServicePointManager.DefaultConnectionLimit = 1000;

            httpClient = new HttpClient(handler);

            if (_prodServer)
            {
                var p = Process.Start(processStartInfo);
                if (p == null)
                {
                    Console.WriteLine("Could not start server");
                    throw new Exception("Could not start server");
                }
            }
               

            PollHttpRequest(httpClient, URL).Wait();

            _rootDirectory = Directory.CreateTempSubdirectory().FullName;
            _testFilePath = Path.Combine(_rootDirectory, _testFileName); // Example path 
           
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(_testFilePath)!);

            randomString = GenerateRandomString(fileSize);

            for (int i=1; i<=maxFiles; i++)
                File.WriteAllText($"{_testFilePath}{i}.css", randomString);         

            var channel = GrpcChannel.ForAddress(URL, new GrpcChannelOptions
            {
                HttpClient = httpClient
            });

            _client = new WebViewIPC.WebViewIPCClient(channel);
            _browser = new BrowserIPC.BrowserIPCClient(channel);
        }
       // [Benchmark]
        public void CreateClientBenchmark()
        {
            string id = Guid.NewGuid().ToString();
            var response = _client.CreateWebView(new CreateWebViewRequest { Id = id });               
            _client.Shutdown(new IdMessageRequest { Id = id });
        }

        //[Benchmark]
        public void CreateAndReadClientBenchmark()
        {
            string id = Guid.NewGuid().ToString();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));  // shutdown waiting 20 seconds for tasks to cancel
            var response = _client.CreateWebView(new CreateWebViewRequest { Id = id });
            // foreach (var message in response.ResponseStream.ReadAllAsync(cts.Token).ToBlockingEnumerable())
            foreach (var message in response.ResponseStream.ReadAllAsync().ToBlockingEnumerable())
            {
                if (message.Response == "created:")
                {
                    _client.Shutdown(new IdMessageRequest { Id = id });
                    break;
                }

                break;
            }
        }

        // | CreateAndReadBrowserClientBenchmark | 313.2 ms | 6.08 ms | 9.10 ms | 512 byte message 
        //   CreateAndReadBrowserClientBenchmark | 317.7 ms | 4.29 ms | 4.01 ms | 1024 byte message
        // | CreateAndReadBrowserClientBenchmark | 310.8 ms | 4.23 ms | 3.95 ms | 1024
        // | CreateAndReadBrowserClientBenchmark | 358.1 ms | 2.80 ms | 2.62 ms | 1000 messages x 10240 bytes
        // | CreateAndReadBrowserClientBenchmark | 359.1 ms | 3.83 ms | 3.59 ms | 1000 messages x 10240 bytes
        //[Benchmark]
        public void CreateAndReadBrowserClientBenchmark()
        {
            int count = 0;
            int max = 1000;
            string id = Guid.NewGuid().ToString();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));  // shutdown waiting 20 seconds for tasks to cancel
            var response = _client.CreateWebView(new CreateWebViewRequest { Id = id });
            foreach (var message in response.ResponseStream.ReadAllAsync(cts.Token).ToBlockingEnumerable())
            //foreach (var message in response.ResponseStream.ReadAllAsync().ToBlockingEnumerable())
            {
                if (message.Response == "created:")
                {
                    _ = Task.Run(() => {
                        for (int i = 1; i <= max; i++)
                            _browser.SendMessage(new SendSequenceMessageRequest { ClientId = id, Id=id, Sequence = (uint)i, Message = $"Message {i} {randomString}", IsPrimary=true, Url="url", Cookies="" });
                    } );

                }
                else if (message.Response.StartsWith("Message"))
                {
                    count++;
                    if (int.Parse (message.Response.Split(" ")[1]) == max)
                    {
                        _client.Shutdown(new IdMessageRequest { Id = id });
                        break;
                    }
                   
                }
            }
            Console.WriteLine("Read " + count.ToString() + " messages");
        }
        string etagHashString = "abcdef123456";
        //[Benchmark]
        public void String1()
        {
           

            // String concatenation
            string etag1 = '"' + etagHashString + '"';

        }
        //[Benchmark]
        public void String12()
        {
        

            // String interpolation
            string etag2 = $"\"{etagHashString}\"";
        }


        // | ReadFilesClientBenchmark | 677.8 ms | 13.48 ms | 13.85 ms | 1000 files of len 
        // | ReadFilesClientBenchmark | 757.7 ms | 14.05 ms | 14.43 ms | 1000 files of len 10240
        // | ReadFilesClientBenchmark | 736.5 ms | 12.93 ms | 12.09 ms | 1000 files of len 10240
        // | ReadFilesClientBenchmark | 1.213 s | 0.0240 s | 0.0329 s | 1000 files of len 102400
        // | ReadFilesClientBenchmark | 1.136 s | 0.0221 s | 0.0324 s | 1000 files of len 102400 with optimized file reads dotnet 8
        // | ReadFilesClientBenchmark | 1.100 s | 0.0154 s | 0.0128 s | 1000 files of len 102400 with optimized file reads dotnet 9
        // | ReadFilesClientBenchmark | 1.068 s | 0.0206 s | 0.0238 s | 1000 files of len 102400 with optimized file reads dotnet 9
        // | ReadFilesClientBenchmark | 1.139 s | 0.0201 s | 0.0178 s | 1000 files of len 102400 with optimized file reads dotnet 8


        // | ReadFilesClientBenchmark | 99.45 ms | 2.535 ms | 7.314 ms | 100 files of len 102400 in parallel dotnet 8
        // | ReadFilesClientBenchmark | 134.3 ms | 2.67 ms | 6.09 ms | 100 files of len 102400 in series dotnet 8

        // | Method                   | Mean    | Error    | StdDev   | Gen0      | Gen1      | Gen2      | Allocated |
        // | ReadFilesClientBenchmark | 1.178 s | 0.0646 s | 0.0427 s | 6000.0000 | 5000.0000 | 5000.0000 |  33.96 MB | 100 files of len 102400 in series dotnet 8
        // | ReadFilesClientBenchmark | 1.197 s | 0.0601 s | 0.0397 s | 6000.0000 | 5000.0000 | 5000.0000 |  34.16 MB |
        // | ReadFilesClientBenchmark | 1.269 s | 0.0919 s | 0.0608 s | 6000.0000 | 5000.0000 | 5000.0000 |  34.17 MB |
        // | ReadFilesClientBenchmark | 567.5 ms | 70.18 ms | 46.42 ms | 6000.0000 | 5000.0000 | 5000.0000 |  34.15 MB |
        // | ReadFilesClientBenchmark | 309.8 ms | 21.67 ms | 11.33 ms | 6500.0000 | 5500.0000 | 5500.0000 |  34.15 MB | Set log level to Warning
        // | ReadFilesClientBenchmark | 551.1 ms | 134.3 ms | 88.85 ms | 5000.0000 | 4500.0000 | 4000.0000 |  34.17 MB | 100 files in parallel, log level warning
        // | ReadFilesClientBenchmark | 389.3 ms | 52.40 ms | 27.40 ms | 4500.0000 | 4000.0000 | 3500.0000 |  34.17 MB | 100 files in parallel, cancel createwebview
        // | ReadFilesClientBenchmark | 1.260 m | 1.417 m | 0.8434 m | 884.07 KB |
        // | ReadFilesClientBenchmark | 743.0 ms | 121.0 ms | 72.00 ms | 4000.0000 | 3000.0000 | 3000.0000 |  34.15 MB | 100 files in parallel and parallel read
        // | ReadFilesClientBenchmark | 552.0 ms | 120.6 ms | 71.76 ms | 5000.0000 | 4500.0000 | 4000.0000 |   34.2 MB | 100 files in parallel and CreateBounded<FileReadRequest>(1);
        // | ReadFilesClientBenchmark | 477.1 ms | 62.07 ms | 183.0 ms | 423.1 ms | 6000.0000 | 5000.0000 | 5000.0000 |   34.1 MB | 100 files in series server in debug
        // | ReadFilesClientBenchmark | 489.9 ms | 29.91 ms | 82.38 ms | 1000.0000 |     34 MB | 100 files in parallel server in debug 
        // | ReadFilesClientBenchmark | 490.4 ms | 25.77 ms | 72.27 ms | 470.3 ms | 1000.0000 |  34.11 MB |100 files in parallel and CreateBounded<FileReadRequest>(1); server in debug
        // | ReadFilesClientBenchmark | 514.6 ms | 37.61 ms | 107.3 ms | 483.8 ms | 1000.0000 |  34.25 MB |100 files in parallel and CreateBounded<FileReadRequest>(100); server in debug
        // | ReadFilesClientBenchmark | 450.9 ms | 15.68 ms | 42.65 ms | 1000.0000 |   34.1 MB |100 files in parallel and CreateBounded<FileReadRequest>(Environment.ProcessorCount); server in debug
        // | ReadFilesClientBenchmark | 310.4 ms | 15.19 ms | 42.59 ms | 1000.0000 | 500.0000 |   34.1 MB |100 files in parallel and CreateBounded<FileReadRequest>(Environment.ProcessorCount); server in release
        // | ReadFilesClientBenchmark | 337.7 ms | 21.76 ms | 59.20 ms | 319.3 ms | 1000.0000 |     34 MB |100 files in parallel and serial FileReader server in release
        // | ReadFilesClientBenchmark | 53.14 ms | 1.036 ms | 2.836 ms | 4600.0000 | 3800.0000 | 3400.0000 |  34.06 MB | 100 files in parallel and serial FileReader production server 
        // | ReadFilesClientBenchmark | 36.36 ms | 1.157 ms | 3.337 ms | 3000.0000 | 2600.0000 | 1800.0000 |   34.2 MB | 100 files in parallel and parallel FileReader production server

        // Crashing with 1000 files
        // | ReadFilesClientBenchmark | 67.98 ms | 2.285 ms | 6.592 ms | 5666.6667 | 5000.0000 | 3000.0000 |  68.21 MB |200 files in parallel and parallel FileReader production server
        // | ReadFilesClientBenchmark | 137.7 ms | 5.68 ms | 16.65 ms | 132.4 ms | 8000.0000 | 7000.0000 | 3000.0000 | 136.06 MB |400 files in parallel and parallel FileReader production server
        // | ReadFilesClientBenchmark | 205.4 ms | 6.71 ms | 19.58 ms | 12000.0000 | 10000.0000 | 5000.0000 | 204.11 MB |600 files in parallel and parallel FileReader production server
        // 800 files fails
        // | ReadFilesClientBenchmark | 227.5 ms | 5.61 ms | 16.27 ms | 15000.0000 | 13000.0000 | 6000.0000 | 238.13 MB |700 files in parallel and parallel FileReader production server
        //   ReadFilesClientBenchmark | 261.7 ms | 10.20 ms | 29.60 ms | 15000.0000 | 13000.0000 | 6000.0000 | 255.08 MB | 750 files in parallel and parallel FileReader production server
        // 775 fails
        // 775 fails with files of size 10240 
        // 775 fails with files of size 10240 and serial FileReader
        // 775 fails with files of size 10240 and parallel FileReader and rate limited server
        //| ReadFilesClientBenchmark | 1.686 s | 0.0336 s | 0.0709 s | 4000.0000 | 1000.0000 |  51.35 MB  | 700 files of size 10240 and parallel FileReader and rate limited server permit limit 10
        //700 files of size 10240 and parallel FileReader and rate limited server permit limit 100 fails
        //| ReadFilesClientBenchmark | 1.480 s | 0.0388 s | 0.1138 s | 4000.0000 | 1000.0000 |  49.22 MB | 700 files of size 10240 and parallel FileReader and no rate limit

        //| ReadFilesClientBenchmark | 67.40 ms | 1.916 ms | 5.373 ms | 65.73 ms | 4000.0000 | 1000.0000 |  49.22 MB |700 files in parallel and parallel FileReader production server *** Removed writeln exception
        // | ReadFilesClientBenchmark | 65.74 ms | 1.392 ms | 3.811 ms | 64.57 ms | 4000.0000 | 1000.0000 |   49.2 MB |700 files in parallel and parallel FileReader production server *** Removed writeln exception
        // | ReadFilesClientBenchmark | 249.1 ms | 9.00 ms | 26.25 ms | 15000.0000 | 13000.0000 | 6000.0000 | 238.02 MB |700 files 102400 in parallel and parallel FileReader production server *** Removed writeln exception
        // | ReadFilesClientBenchmark | 7.521 s | 0.5679 s | 1.638 s | 17000.0000 | 14000.0000 | 7000.0000 | 272.04 MB |800 files with rate monitor = 100
        // | ReadFilesClientBenchmark | 1.834 s | 0.0379 s | 0.1074 s | 800 files no rate limiter running Release mode
        // | ReadFilesClientBenchmark | 2.559 s | 0.0786 s | 0.2319 s | 1000 files no rate limiter running Release mode
        // | ReadFilesClientBenchmark | 4.567 s | 0.0898 s | 0.1550 s | 2000 files no rate limiter running Release mode
        // | ReadFilesClientBenchmark | 683.0 ms | 16.42 ms | 48.41 ms | 2000 files 102400 in parallel and parallel FileReader production server *** Removed writeln exception
        // | ReadFilesClientBenchmark | 220.6 ms | 5.75 ms | 16.87 ms ||700 files 102400 in parallel and parallel FileReader production server *** Removed writeln exception and optimizations i.e. async
        // | ReadFilesClientBenchmark | 1.814 s | 0.0360 s | 0.0911 s | 1.852 s |  5000 files 102400 in parallel and parallel FileReader production server *** Removed writeln exception
        // | ReadFilesClientBenchmark | 3.614 s | 0.0703 s | 0.1155 s | 10000 files 102400 in parallel and parallel FileReader production server *** Removed writeln exception
        [Benchmark]
        public void ReadFilesClientBenchmark()
        {     
            string id = Guid.NewGuid().ToString();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30000));  // shutdown waiting 20 seconds for tasks to cancel
            var response = _client.CreateWebView(new CreateWebViewRequest { Id = id, EnableMirrors=false, HtmlHostPath="wwwroot" }, null,null, cts.Token);
         
            var wrapper = new HttpClientWrapper(httpClient);
            try
            {
                foreach (var message in response.ResponseStream.ReadAllAsync(/*cts.Token*/).ToBlockingEnumerable())
                {
                    if (message.Response == "created:")
                    {
                        FileReader.AttachFileReader(_client.FileReader(), cts, id, new PhysicalFileProvider(_rootDirectory + "/wwwroot"));
                       

                        List<Task> tasks = new List<Task>();
                        for (int i = 1; i <= maxFiles; i++)
                        {
                            string url = $"{URL}/{id}/{_testFileName}{i}.css";
                            tasks.Add(Task.Run(async () =>

                            //Task.Run(async () =>
                            {
                                var data = await wrapper.GetWithRetryAsync(url);
                              
                            }));//.Wait();
                        }

                        Task.WaitAll(tasks.ToArray());
                        cts.Cancel();
                    }

                }
            }
            catch (Exception ex)
            {
                if (ex.InnerException is not OperationCanceledException)
                   Console.WriteLine(ex.ToString());
            }
            finally
            {
                //_client.Shutdown(new IdMessageRequest { Id = id });
            }
            Debug.Assert(wrapper.count == maxFiles);
            Debug.Assert(wrapper.bytes == maxFiles * fileSize);
            //Console.WriteLine($"Read {wrapper.count} files total bytes {wrapper.bytes}");
        }


        [GlobalCleanup]
        public void Cleanup()
        {
            try
            {
                // Clean up any temporary files created
                File.Delete(_testFilePath);
                Directory.Delete(_testFilePath.Replace("site",""));

            }
            catch { }
        }
        [Config(typeof(Config))]
        public class Config : ManualConfig
        {
            public Config()
            {
                //AddJob(Job.Default
                //    .WithIterationCount(10) // Adjust as needed
                //    .WithWarmupCount(5) // Adjust as needed
                //);
                AddJob(Job.Default);
                  
                AddDiagnoser(MemoryDiagnoser.Default);
            }
        }

        public class HttpClientWrapper
        {
            private const int MaxRetries = 1;
            private const int RetryDelayMilliseconds = 1000;
            private object lockObject = new object();
            private readonly HttpClient httpClient;

            public HttpClientWrapper(HttpClient httpClient)
            {
                this.httpClient = httpClient;
            }

            public async Task<string> GetWithRetryAsync(string url)
            {
                int attempts = 0;
                while (attempts < MaxRetries)
                {
                    try
                    {
                        var response = await httpClient.GetAsync(url);
                        if (response.IsSuccessStatusCode)
                        {
                            var data = await response.Content.ReadAsStringAsync();
                            lock (lockObject)
                            {
                                bytes += data.Length;
                                //Console.WriteLine(data);
                                count++;
                            }
                            return data;
                        }
                        else
                        {
                            Console.WriteLine($"Attempt {attempts + 1} failed. Status code: {response.StatusCode}");
                        }
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.WriteLine($"Attempt {attempts + 1} failed. Error: {ex.Message}");
                    }

                    attempts++;
                    if (attempts < MaxRetries)
                    {
                        await Task.Delay(RetryDelayMilliseconds);
                    }
                }

                throw new Exception($"Failed to get successful response after {MaxRetries} attempts");
            }

            // Assuming these are class-level variables
            public int bytes;
            public int count;
        }

#if DEBUG
        static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(ClientBenchmarks).Assembly).Run(args, new DebugInProcessConfig());
#else
        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<ClientBenchmarks>();
            Console.WriteLine(summary);
            Console.ReadLine();
        }
#endif
    }
}
