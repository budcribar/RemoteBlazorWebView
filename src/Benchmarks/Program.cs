using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
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
				var summary = BenchmarkRunner.Run<FileReadInitRequest>();
			}
		}
	}
}
