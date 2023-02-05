using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
	[SimpleJob(RuntimeMoniker.Net60)]
	[RPlotExporter]
	public class FileReadPerformanceTests
	{
		private readonly ILogger<RemoteWebViewService> _logger = LoggerFactory.Create(builder => { builder.AddConsole(); }).CreateLogger<RemoteWebViewService>();
		private ServiceState _serviceState = default!;
		private readonly string _filePath = "wwwroot/testfile.txt";
		private readonly byte[] _fileContents = new byte[1024];

		[GlobalSetup]
		public void Setup()
		{
			// Create a test file with 1024 bytes of data
			using var fileStream = File.Create(_filePath);
			fileStream.Write(_fileContents);
			_serviceState = new ServiceState(_logger, false);
		}

		[GlobalCleanup]
		public void Cleanup()
		{
			// Delete the test file
			File.Delete(_filePath);
		}

		[Benchmark]
		public async Task ReadFile()
		{
			// Add the file to the dictionary and set the reset event
			_serviceState.FileDictionary.TryAdd(_filePath, new FileEntry { ResetEvent = new ManualResetEventSlim() });
			_serviceState.FileDictionary[_filePath].ResetEvent.Set();

			// Read the file contents from the Pipe
			using var fileStream = _serviceState.FileDictionary[_filePath].Pipe.Reader.AsStream();
			var fileContents = new byte[1024];
			await fileStream.ReadAsync(fileContents);
		}

		public class Program
		{
			public static void Main(string[] args)
			{
                var channel = GrpcChannel.ForAddress(@"https://linux.remoteblazorwebview.com/",//:8585",
                       new GrpcChannelOptions
                       {
                           HttpHandler = new SocketsHttpHandler
                           {
                               PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                               KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                               KeepAlivePingTimeout = TimeSpan.FromSeconds(50),   // 30 seconds is not enough to pass stress tests
                               EnableMultipleHttp2Connections = true
                           }
                       });

                var client = new WebViewIPC.WebViewIPCClient(channel);
				Google.Protobuf.WellKnownTypes.Empty request = new Google.Protobuf.WellKnownTypes.Empty();
                var ids = client.GetIds(request);

                var summary = BenchmarkRunner.Run<FileReadInitRequest>();
			}
		}
	}
}
