using System;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Grpc.Core;
using PeakSWC.RemoteWebView;

namespace PerformanceTests
{
	public class FileReadInitRequestBenchmark
	{
		private static readonly Channel channel = new Channel("localhost", 50051, ChannelCredentials.Insecure);
		private static readonly RemoteWebViewService.RemoteWebViewServiceClient client = new RemoteWebViewService.RemoteWebViewServiceClient(channel);

		[Benchmark]
		public async Task FileReadInitRequest()
		{
			// Replace the file path and guid with valid values
			var request = new FileReadInitRequest { File = "C:\\test\\test.txt", Guid = "123456" };
			await client.FileReadInitAsync(request);
		}
	}

	class Program
	{
		static void Main(string[] args)
		{
			var summary = BenchmarkRunner.Run<FileReadInitRequestBenchmark>();
		}
	}
}



