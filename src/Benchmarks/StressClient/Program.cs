using Grpc.Net.Client;
using System;
using Grpc.Net.Client.Web;
using Google.Protobuf.WellKnownTypes;
using PeakSWC.RemoteWebView;
using Grpc.Core;

namespace StressClient
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Usage: program <numBuffers> <minSize> <maxSize>");
                return;
            }

            if (!int.TryParse(args[0], out int numBuffers) ||
                !int.TryParse(args[1], out int minSize) ||
                !int.TryParse(args[2], out int maxSize))
            {
                Console.WriteLine("All arguments must be integers.");
                return;
            }

            if (numBuffers <= 0 || minSize <= 0 || maxSize <= 0 || minSize > maxSize)
            {
                Console.WriteLine("Invalid argument values. Ensure all values are positive and minSize <= maxSize.");
                return;
            }

            Console.WriteLine($"Number of buffers: {numBuffers}");
            Console.WriteLine($"Minimum size: {minSize}");
            Console.WriteLine($"Maximum size: {maxSize}");

            using var handler = new SocketsHttpHandler
            {
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                KeepAlivePingDelay = TimeSpan.FromSeconds(90),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(60),
                EnableMultipleHttp2Connections = true
            };
          
            using var httpClient = new HttpClient(handler);
           
            using var channel = GrpcChannel.ForAddress("https://localhost:5001", new GrpcChannelOptions
            {
                HttpClient = httpClient
            });
           
            var client = new WebViewIPC.WebViewIPCClient(channel);
            string id = Guid.NewGuid().ToString();

            try
            {
                var response = client.CreateWebView(new CreateWebViewRequest { Id = id });

                await foreach (var message in response.ResponseStream.ReadAllAsync())
                {
                    if (message.Response == "created:")
                    {
                        for (int i = 0; i < numBuffers; i++)
                        {
                            await Task.Delay(1000 * Random.Shared.Next(1));
                        }
                        client.Shutdown(new IdMessageRequest { Id = id });
                        break;
                    }
                    Console.WriteLine($"Creation message for {id}: {message.Response}");
                    break;
                }

            }
            catch (Exception ex) { 
                Console.WriteLine(ex.ToString()); 
            }
           
        }
    }
}
