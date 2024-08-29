using Grpc.Net.Client;
using System;
using Grpc.Net.Client.Web;
using Google.Protobuf.WellKnownTypes;
using PeakSWC.RemoteWebView;
using Grpc.Core;
using System.Diagnostics;

namespace StressClient
{
    internal class Program
    {
        private static EventLog? eventLog;

        static async Task Main(string[] args)
        {
            SetupEventLog();

            if (args.Length != 3)
            {
                LogEvent("Usage: program <numBuffers> <minSize> <maxSize>", EventLogEntryType.Error);
                return;
            }

            if (!int.TryParse(args[0], out int numBuffers) ||
                !int.TryParse(args[1], out int minSize) ||
                !int.TryParse(args[2], out int maxSize))
            {
                LogEvent("All arguments must be integers.", EventLogEntryType.Error);
                return;
            }

            if (numBuffers <= 0 || minSize <= 0 || maxSize <= 0 || minSize > maxSize)
            {
                LogEvent("Invalid argument values. Ensure all values are positive and minSize <= maxSize.", EventLogEntryType.Error);
                return;
            }

            //LogEvent($"Number of buffers: {numBuffers}", EventLogEntryType.Information);
            //LogEvent($"Minimum size: {minSize}", EventLogEntryType.Information);
            //LogEvent($"Maximum size: {maxSize}", EventLogEntryType.Information);

            Stopwatch sw = Stopwatch.StartNew();
            try
            {
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
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var response = client.CreateWebView(new CreateWebViewRequest { Id = id });
                await foreach (var message in response.ResponseStream.ReadAllAsync(cts.Token))
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
                    LogEvent($"Creation message for {id}: {message.Response}", EventLogEntryType.Error);
                    break;
                }
            }
            catch (Exception ex)
            {
                LogEvent(ex.ToString(), EventLogEntryType.Error);
            }
            LogEvent($"Create took {sw.Elapsed}", EventLogEntryType.Information);
        }

        private static void SetupEventLog()
        {
            string source = "StressClientApp";
            string logName = "Application";

            if (!EventLog.SourceExists(source))
            {
                EventLog.CreateEventSource(source, logName);
            }

            eventLog = new EventLog(logName)
            {
                Source = source
            };
        }

        private static void LogEvent(string message, EventLogEntryType entryType)
        {
            eventLog?.WriteEntry(message, entryType);
        }
    }
}