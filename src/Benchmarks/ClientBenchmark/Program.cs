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
using System.Net.Security;
using System.Net.Quic;
using System.Runtime.Versioning;
using System.Net.Sockets;


namespace ClientBenchmark
{
    public class ClientBenchmarks
    {
        private string URL = "https://192.168.1.35:5002";
        //private string URL = "https://127.0.0.1:5001";
        //private string URL = "https://localhost:5001";
        //private string URL = "https://remotewebviewserver.azurewebsites.net/";
        private bool _prodServer = false;
        private int fileSize = 102400;
        private int maxFiles = 700;
        private bool useHttp3 = false;



        // what happens when you have multiple reads of the same file?
        private string _testGuid;
        private string _testFilePath;
        private string _rootDirectory;
        private string _testFileName = "wwwroot/css/site";
        private WebViewIPC.WebViewIPCClient _client;
        private BrowserIPC.BrowserIPCClient _browser;
        private string randomString;
        private HttpClient httpClient;
      
        [GlobalSetup]
        public void Setup()

        {
            if (_prodServer)
                Utilities.KillExistingProcesses("RemoteWebViewService");

            var processStartInfo = new ProcessStartInfo
            {
#if DEBUG
                FileName = @"..\..\..\..\..\RemoteWebViewService\bin\publishNoAuth\RemoteWebViewService.exe",
#else
                FileName = @"..\..\..\..\..\..\..\..\..\RemoteWebViewService\bin\publishNoAuth\RemoteWebViewService.exe",
#endif
                RedirectStandardOutput = true
            };
            if (_prodServer)
            {
                var p = Process.Start(processStartInfo);
                if (p == null)
                {
                    Console.WriteLine("Could not start server");
                    throw new Exception("Could not start server");
                }
            }
            AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", true);
            var handler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(90),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(60),
                MaxConnectionsPerServer = 1000,
                EnableMultipleHttp2Connections = true,
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls13 | System.Security.Authentication.SslProtocols.Tls12,
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                },
            };

            //ServicePointManager.DefaultConnectionLimit = 1000;

            httpClient = new HttpClient(handler);
            if (useHttp3)
            {
                httpClient.DefaultRequestVersion = HttpVersion.Version30;
                httpClient.DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact;//RequestVersionExact;//.;.;/ 9o.RequestVersionOrLower;
            }
           
            try
            {
           
               
                var response = httpClient.GetAsync(URL).Result;
                Console.WriteLine($"Response status: {response.StatusCode}");
                Console.WriteLine($"Protocol used: {response.Version}");

                // Print all headers
                foreach (var header in response.Headers)
                {
                    Console.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                // Check for Alt-Svc header
                if (response.Headers.TryGetValues("Alt-Svc", out var altSvcValues))
                {
                    Console.WriteLine($"Alt-Svc: {string.Join(", ", altSvcValues)}");
                    if (altSvcValues.Any(v => v.StartsWith("h3=")))
                    {
                        Console.WriteLine("HTTP/3 is supported by the server!");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error: {e.Message}");
                if (e.InnerException != null)
                    Console.WriteLine($"Inner exception: {e.InnerException.Message}");
            }

            // Utilities.TryVariousPorts();


            Utilities.PollHttpRequest(httpClient, URL).Wait();

            _rootDirectory = Directory.CreateTempSubdirectory().FullName;
            _testFilePath = Path.Combine(_rootDirectory, _testFileName); // Example path 
           
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(_testFilePath)!);

            randomString = Utilities.GenerateRandomString(fileSize);

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

        // | ReadFilesClientBenchmark | 2.116 s | 0.0124 s | 0.0116 s | 200 files in parallel and parallel FileReader production server (try http3)
        // | | ReadFilesClientBenchmark | 68.58 ms | 2.289 ms | 6.750 ms |200 files in parallel and parallel FileReader production server 
        // | ReadFilesClientBenchmark | 69.23 ms | 1.383 ms | 2.028 ms |200 files in parallel and parallel FileReader production server 
        // | ReadFilesClientBenchmark | 454.2 ms | 25.40 ms | 74.88 ms | 100 files 102400 bytes http3 release server
        // | ReadFilesClientBenchmark | 438.1 ms | 13.75 ms | 39.01 ms | 100 files 102400 bytes http2 release server
        // | ReadFilesClientBenchmark | 438.1 ms | 13.75 ms | 39.01 ms | 100 files 102400 bytes http2 prod server
        // | ReadFilesClientBenchmark | 35.08 ms | 0.661 ms | 0.552 ms | 100 files 102400 bytes http2 prod server
        // | ReadFilesClientBenchmark | 36.36 ms | 0.526 ms | 0.771 ms | 100 files 102400 bytes http2 prod server
        // | ReadFilesClientBenchmark | 36.44 ms | 0.714 ms | 0.904 ms | 100 files 102400 bytes http2 prod server
        // | ReadFilesClientBenchmark | 36.01 ms | 0.645 ms | 0.662 ms |
        // | ReadFilesClientBenchmark | 3.172 s | 0.1604 s | 0.4445 s | 100 files 102400 bytes http2 "https://remotewebviewserver.azurewebsites.net/";
        // | ReadFilesClientBenchmark | 33.84 ms | 0.834 ms | 2.434 ms |  100 files 102400 bytes http2 prod server !!! net 9
        // | ReadFilesClientBenchmark | 33.76 ms | 0.670 ms | 1.934 ms | 100 files 102400 bytes http2 prod server !!! net 9
        // | ReadFilesClientBenchmark | 405.9 ms | 22.92 ms | 64.64 ms | 100 files 102400 bytes http2 prod server !!! net 9 branch using net 8 server
        // | ReadFilesClientBenchmark | 36.48 ms | 1.895 ms | 5.588 ms | 35.01 ms | 100 files 102400 bytes http2 prod server !!! net 9
        // | ReadFilesClientBenchmark | 34.21 ms | 1.063 ms | 3.066 ms | 100 files 102400 bytes http2 prod server !!! net 9
        // | ReadFilesClientBenchmark | 32.08 ms | 0.653 ms | 1.777 ms | 31.55 ms |100 files 102400 bytes http2 prod server !!! net 9 Third run a charm got faster each time
        // | ReadFilesClientBenchmark | 101.4 ms | 5.94 ms | 16.95 ms | 100 files 102400 bytes http2 release server
        // | ReadFilesClientBenchmark | 35.60 ms | 1.269 ms | 3.662 ms | 100 files 102400 bytes http2 prod server !!! net 9 return after len read
        // | ReadFilesClientBenchmark | 34.82 ms | 1.110 ms | 3.237 ms | 100 files 102400 bytes http2 prod server !!! net 9 return after len read 4th run

        // | ReadFilesClientBenchmark | 33.30 ms | 1.024 ms | 2.920 ms | 32.38 ms |
        // | ReadFilesClientBenchmark | 33.85 ms | 1.021 ms | 2.913 ms |
        // | ReadFilesClientBenchmark | 32.46 ms | 0.648 ms | 1.476 ms |

        // | ReadFilesClientBenchmark | 33.68 ms | 0.670 ms | 1.935 ms |
        // | ReadFilesClientBenchmark | 33.94 ms | 0.675 ms | 1.525 ms | leave server running
        // | ReadFilesClientBenchmark | 35.01 ms | 1.024 ms | 2.954 ms | 34.10 ms |
        // | ReadFilesClientBenchmark | 34.06 ms | 0.730 ms | 2.140 ms |
        // Fails on 1000; pass on 100,200,400, timeout on 800

        //  dotnet8
        // | ReadFilesClientBenchmark | 231.2 ms | 5.94 ms | 17.03 ms | 700 files 102400 in parallel and parallel FileReader early semaphore release production server
        //| ReadFilesClientBenchmark | 220.2 ms | 5.24 ms | 15.29 ms |700 files 102400 in parallel and parallel FileReader early semaphore release production server
        // | ReadFilesClientBenchmark | 246.3 ms | 8.01 ms | 23.25 ms | 700 files 102400 in parallel and parallel FileReader late semaphore release production server
        // | ReadFilesClientBenchmark | 237.6 ms | 7.09 ms | 20.69 ms | 700 files 102400 in parallel and parallel FileReader late semaphore release production server
        // | ReadFilesClientBenchmark | 224.4 ms | 5.39 ms | 15.02 ms | 700 files 102400 in parallel and parallel FileReader late semaphore release production server
        // | ReadFilesClientBenchmark | 222.6 ms | 5.07 ms | 14.79 ms |700 files 102400 in parallel and parallel FileReader early semaphore release production server
        // | ReadFilesClientBenchmark | 223.1 ms | 4.95 ms | 14.45 ms |700 files 102400 in parallel and parallel FileReader early semaphore release production server

        // dotnet 9
        // | ReadFilesClientBenchmark | 218.3 ms | 4.36 ms | 11.94 ms | 700 files 102400 in parallel and parallel FileReader early semaphore release production server
        // | ReadFilesClientBenchmark | 201.4 ms | 3.76 ms | 10.36 ms | 700 files 102400 in parallel and parallel FileReader early semaphore release production server

        // dotnet 9 rc1
        // | ReadFilesClientBenchmark | 216.2 ms | 6.12 ms | 17.94 ms |700 files 102400 in parallel and parallel FileReader early semaphore release production server
        // | ReadFilesClientBenchmark | 228.0 ms | 5.40 ms | 15.57 ms |
        // | ReadFilesClientBenchmark | 229.7 ms | 6.49 ms | 18.63 ms |

        // added ConfigureAwait(false)
        //| ReadFilesClientBenchmark | 223.3 ms | 6.18 ms | 17.92 ms || ReadFilesClientBenchmark | 223.3 ms | 6.18 ms | 17.92 ms | 700 files 102400 in parallel and parallel FileReader early semaphore release production server
        //| ReadFilesClientBenchmark | 227.7 ms | 5.99 ms | 17.39 ms |
        //| ReadFilesClientBenchmark | 221.2 ms | 6.12 ms | 17.85 ms |
        // 1000 files failed
        //| ReadFilesClientBenchmark | 223.5 ms | 5.99 ms | 17.28 ms |
        // 1000 files failed

        // <ServerGarbageCollection>true</ServerGarbageCollection>
        //| ReadFilesClientBenchmark | 223.5 ms | 6.10 ms | 17.61 ms |
        //| ReadFilesClientBenchmark | 224.1 ms | 5.13 ms | 14.97 ms |
        //| ReadFilesClientBenchmark | 224.5 ms | 5.72 ms | 16.33 ms |

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
                        FileReader.AttachFileReader(_client.FileReader(), cts, id, new PhysicalFileProvider(_rootDirectory + "/wwwroot"), (x) => { });//Console.Write($"File reader threw {x.Message}"));
                       

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
                AddJob(Job.Default);     
                AddDiagnoser(MemoryDiagnoser.Default);
            }
        }

#if DEBUG
        public static  void Main(string[] args) { 
        //public static async Task Main(string[] args) { 
            BenchmarkSwitcher.FromAssembly(typeof(ClientBenchmarks).Assembly).Run(args, new DebugInProcessConfig());
        }
#else
        // public static async Task Main(string[] args)
       
        public static void Main(string[] args)
        {
         
            var summary = BenchmarkRunner.Run<ClientBenchmarks>();
            Console.WriteLine(summary);
            Console.ReadLine();
        }
#endif
    }
}
