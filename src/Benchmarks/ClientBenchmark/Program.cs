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

namespace ClientBenchmark
{
    public class ClientBenchmarks
    {
        private string _testGuid;
        private string _testFilePath;
        private string _rootDirectory;
        private string _testFileName = "wwwroot/css/site.css";
        private WebViewIPC.WebViewIPCClient _client;
        private BrowserIPC.BrowserIPCClient _browser;
        private string randomString;
        private HttpClient httpClient;
        private string URL = "https://localhost:5001";
        private bool _prodServer = true;

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
                EnableMultipleHttp2Connections = true
            };


            httpClient = new HttpClient(handler);

            if (_prodServer)
                Process.Start(processStartInfo);

            PollHttpRequest(httpClient, URL).Wait();

            _rootDirectory = Directory.CreateTempSubdirectory().FullName;
            _testFilePath = Path.Combine(_rootDirectory, _testFileName); // Example path 
           
            // Create the directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(_testFilePath)!);

            randomString = GenerateRandomString(102400);
            File.WriteAllText(_testFilePath, randomString);         

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


        // | ReadFilesClientBenchmark | 677.8 ms | 13.48 ms | 13.85 ms | 1000 files of len 
        // | ReadFilesClientBenchmark | 757.7 ms | 14.05 ms | 14.43 ms | 1000 files of len 10240
        // | ReadFilesClientBenchmark | 736.5 ms | 12.93 ms | 12.09 ms | 1000 files of len 10240
        // | ReadFilesClientBenchmark | 1.213 s | 0.0240 s | 0.0329 s | 1000 files of len 102400
        [Benchmark]
        public void ReadFilesClientBenchmark()
        {
            int count = 0;
            int max = 1000;
            string id = Guid.NewGuid().ToString();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30000));  // shutdown waiting 20 seconds for tasks to cancel
            var response = _client.CreateWebView(new CreateWebViewRequest { Id = id, EnableMirrors=false, HtmlHostPath="wwwroot" });
            var bytes = 0;
            foreach (var message in response.ResponseStream.ReadAllAsync(cts.Token).ToBlockingEnumerable())
            {
                if (message.Response == "created:")
                {
                    AttachFileReader(cts, id, new PhysicalFileProvider(_rootDirectory+"/wwwroot"));
                    string url = $"{URL}/{id}/{_testFileName}";
                    for (int i = 1; i <= max; i++) { 
                        var data = httpClient.GetStringAsync(url).Result;
                        bytes += data.Length;
                        //Console.WriteLine(data);
                        count++;
                    }

                    _client.Shutdown(new IdMessageRequest { Id = id });
                }
                
            }
            Console.WriteLine($"Read {count} files total bytes {bytes}");
        }

        private void AttachFileReader(CancellationTokenSource cts, string id, IFileProvider fileProvider)
        {
            _ = Task.Factory.StartNew(async () =>
            {
                var files = _client.FileReader();
                try
                {
                    await files.RequestStream.WriteAsync(new FileReadRequest { Id = id, Init = new() });

                    await foreach (var message in files.ResponseStream.ReadAllAsync(cts.Token))
                    {
                        try
                        {
                            var path = message.Path[(message.Path.IndexOf('/') + 1)..];

                            await files.RequestStream.WriteAsync(new FileReadRequest { Id = id, Length = new FileReadLengthRequest { Path = message.Path, Length = fileProvider.GetFileInfo(path).Length } });

                            using var stream = fileProvider.GetFileInfo(path).CreateReadStream() ?? null;
                            if (stream == null)
                                await files.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                            else
                            {
                                var buffer = new Byte[8 * 1024];
                                int bytesRead = 0;

                                while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                                {
                                    ByteString bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                    await files.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = bs } });
                                }
                                await files.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                            }

                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine("FileNotFoundException");
                            // TODO Warning to user?
                            await files.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                            //FireDisconnected(ex);
                            await files.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                        }
                    }
                    Console.WriteLine("Done reading files");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    //FireDisconnected(ex);
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            try
            {
                // Clean up any temporary files created
                File.Delete(_testFilePath);
                Directory.Delete(_testFilePath);
            }
            catch { }
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
