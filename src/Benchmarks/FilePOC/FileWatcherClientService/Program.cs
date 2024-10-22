using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Grpc.Net.Client;
using PeakSWC.RemoteWebView;

namespace FileWatcherClient
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Build configuration
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddEnvironmentVariables()
                .Build();

            // Setup Dependency Injection
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection, configuration);

            var serviceProvider = serviceCollection.BuildServiceProvider();

            // Get the logger
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

            // Setup cancellation
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                logger.LogInformation("Cancellation requested via console. Shutting down...");
                eventArgs.Cancel = true; // Prevent the process from terminating immediately.
                cts.Cancel();
            };

            try
            {
                logger.LogInformation("FileWatcher Client Service is starting...");

                // Get the worker and execute
                var worker = serviceProvider.GetRequiredService<Worker>();
                await worker.ExecuteAsync(cts.Token);

                logger.LogInformation("FileWatcher Client Service has stopped.");
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("FileWatcher Client Service is shutting down...");
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "FileWatcher Client Service terminated unexpectedly.");
            }
            finally
            {
                // Ensure to flush and stop internal timers/threads before application exit
                if (serviceProvider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }

        private static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
        {
            // Configure logging to use Event Log
            services.AddLogging(configure =>
            {
                configure.ClearProviders();
                configure.AddEventLog(eventLogSettings =>
                {
                    eventLogSettings.SourceName = "FileWatcherClient"; // Must match the created event source
                    eventLogSettings.LogName = "Application"; // Or your custom log name
                });
            });

            // Add configuration
            services.AddSingleton(configuration);

            // Configure gRPC client
            services.AddSingleton(provider =>
            {
                var grpcServerAddress = configuration["Grpc:ServerAddress"];
                if (string.IsNullOrEmpty(grpcServerAddress))
                {
                    grpcServerAddress = @"https://192.168.1.35:5002";
                    
                }
                var channel = GrpcChannel.ForAddress(grpcServerAddress);
                return new FileWatcherIPC.FileWatcherIPCClient(channel);
            });

            // Register Worker
            services.AddSingleton<Worker>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<Worker>>();
                var filePath = configuration["FileWatcher:WatchFilePath"] ?? @"C:\Users\budcr\source\repos\RemoteBlazorWebView\src\Benchmarks\StressServer\publish\StressServer.exe";
                var runArguments = configuration["FileWatcher:RunArguments"] ?? string.Empty;
                var client = provider.GetRequiredService<FileWatcherIPC.FileWatcherIPCClient>();
                return new Worker(logger, filePath, runArguments, client);
            });
        }
    }
}
