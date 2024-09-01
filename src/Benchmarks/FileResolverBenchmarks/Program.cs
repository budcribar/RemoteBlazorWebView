
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeakSwc.StaticFiles;
using Grpc.Net.Client;
using System;
using Grpc.Net.Client.Web;
using Google.Protobuf.WellKnownTypes;
using PeakSWC.RemoteWebView;
using Grpc.Core;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using BenchmarkDotNet.Configs;

namespace PeakSWC.RemoteWebView.Benchmarks
{
    

    public class FileResolverBenchmarks
    {
        private ConcurrentDictionary<string, ServiceState> _serviceDictionary;
        private ServiceProvider _serviceProvider;
        private RemoteFileResolver _fileResolver;
        private string _testGuid;
        private string _testFilePath;

        [GlobalSetup]
        public void Setup()
        {
            _serviceDictionary = new ConcurrentDictionary<string, ServiceState>();
            _testGuid = Guid.NewGuid().ToString();
            _serviceDictionary.TryAdd(_testGuid, new ServiceState(new LoggerFactory().CreateLogger<RemoteWebViewService>(), false)
            {
                HtmlHostPath = "index.html" // Assume index.html as the root file
            });

            _testFilePath = $"wwwroot/css/site.css"; // Example path 

            // Setup dependency injection
            var services = new ServiceCollection();
            services.AddTransient<RemoteFileResolver>();
            services.AddSingleton(_serviceDictionary);
            services.AddLogging(builder => builder.AddConsole()); // Add logging for the resolver

            _serviceProvider = services.BuildServiceProvider();
            _fileResolver = _serviceProvider.GetRequiredService<RemoteFileResolver>();

            // Create the directory if it doesn't exist
            Directory.CreateDirectory(Path.GetDirectoryName(_testFilePath)!);

            // You might need to create a dummy file at _testFilePath for testing
            File.WriteAllText(_testFilePath, "Test content");
        }

        [Benchmark]
        public void ProcessFileBenchmark()
        {
            var info = _fileResolver.GetFileInfo($"/{_testGuid}/{_testFilePath}");
            Console.WriteLine($"{info.Name} {info.Length}");
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            // Clean up any temporary files created
            File.Delete(_testFilePath); 
            Directory.Delete(_testFilePath);

            _serviceProvider.Dispose();
        }
    }

    public class Program
    {
        static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, new DebugInProcessConfig());

        //public static void Main(string[] args)
        //{
        //    var summary = BenchmarkRunner.Run<FileResolverBenchmarks>();
        //    Console.WriteLine(summary);
        //    Console.ReadKey();
        //}
    }
}
