using Grpc.Net.Client;
using PeakSWC.RemoteWebView; // Essential for accessing WebViewIPC and related classes
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FileSyncClient.Services;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using Microsoft.Extensions.FileProviders;

namespace FileClientApp
{
    class Program
    {
        // Define the server address. Use 'https' for secure connection.
        private const string ServerAddress = "https://localhost:5001"; // Use HTTPS

        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("Starting FileClient...");

            // Parse and validate command-line arguments
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: FileClientApp <clientId>");
                Console.WriteLine("Example: FileClientApp 1e32d82e-2333-49ff-8675-15aa1a088bf1");
                return 1; // Exit with error code
            }

            string clientIdInput = args[0];
            if (!Guid.TryParse(clientIdInput, out Guid clientGuid))
            {
                Console.WriteLine($"Error: '{clientIdInput}' is not a valid GUID.");
                Console.WriteLine("Please provide a valid GUID as the clientId.");
                return 1; // Exit with error code
            }

            // Replace fixed delay with health check
            bool serverIsHealthy = await Utilities.WaitForServerHealthAsync("https://localhost:5001/health");

            if (!serverIsHealthy)
            {
                Console.WriteLine("Server health check failed. Exiting application.");
                return 1; // Exit with error code
            }

            // Create a handler to bypass certificate validation (development only)
            var httpHandler = new HttpClientHandler();
            httpHandler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

            // Create the gRPC channel with the custom handler
            using var channel = GrpcChannel.ForAddress(ServerAddress, new GrpcChannelOptions { HttpHandler = httpHandler });

            // Create the WebViewIPC client
            var grpcClient = new WebViewIPC.WebViewIPCClient(channel);

            var tempDirectory = Path.Combine(AppContext.BaseDirectory, "client_cache");
            Directory.CreateDirectory(tempDirectory);

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
            });
            ILogger<ClientFileSyncManager> logger = loggerFactory.CreateLogger<ClientFileSyncManager>();
            string appRootDir = AppContext.BaseDirectory;

             // Instantiate the FileClient with the provided client GUID
             var fileClient = new ClientFileSyncManager(grpcClient, clientGuid,"index.html", new PhysicalFileProvider(tempDirectory),(e)=> Console.Write(e.Message),  logger);

            // Define the list of files to synchronize by creating them in a temp directory
            var filesToSync = Utilities.CreateTestFiles(tempDirectory);
            Utilities.CreateTestEnvironment(tempDirectory);
            CancellationTokenSource tokenSource = new CancellationTokenSource();
            try
            {
                // Start handling file synchronization requests from the server
                fileClient.HandleServerRequests(tokenSource.Token);

                Console.WriteLine("File synchronization initiated.");
                Console.WriteLine("Press 'q' to quit the application.");

                // Wait for the user to press 'q' to exit
                while (true)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.KeyChar == 'q' || key.KeyChar == 'Q')
                    {
                        Console.WriteLine("Shutdown initiated...");
                        break;
                    }
                }

                // Initiate shutdown
                await fileClient.CloseAsync();

                // TODO
                // Wait for handleFilesTask to complete
                // await handleFilesTask;
            }
            catch (Grpc.Core.RpcException rpcEx)
            {
                Console.WriteLine($"gRPC Error: {rpcEx.Status.Detail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected Error: {ex.Message}");
            }

            Console.WriteLine("FileClient has exited.");
            return 0; // Exit with success code
        }

        

    }
}
