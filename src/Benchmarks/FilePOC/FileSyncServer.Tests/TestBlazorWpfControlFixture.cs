// TestBlazorWpfControlFixture.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Grpc.Net.Client;
using Google.Protobuf.WellKnownTypes;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.Wpf;
using PeakSWC.RemoteWebView;
using Xunit;

namespace WebdriverTestProject
{
    // Fixture class for setup and teardown
    public class TestBlazorWpfControlFixture : IAsyncLifetime
    {
        public static Process? Process { get; private set; }
        public static Window? MainWindow { get; private set; }

        public async Task InitializeAsync()
        {
            string grpcUrl = @"https://localhost:5001/";
            GrpcChannel? channel;
            string? envVarValue = Environment.GetEnvironmentVariable("Rust");
            if (!string.IsNullOrEmpty(envVarValue))
                grpcUrl = @"https://localhost:5002/";

            channel = GrpcChannel.ForAddress(grpcUrl);
            Process = Utilities.StartServer();

            for (int i = 0; i < 10; i++)
            {
                // Wait for server to spin up
                try
                {
                    var ids = new WebViewIPC.WebViewIPCClient(channel).GetIds(new Empty());
                    if (ids.Responses.Count == 0)
                        break;
                }
                catch (Exception)
                {
                    // Wait and retry
                }
                await Task.Delay(1000);
            }

            MainWindow = BlazorWebViewFactory.CreateBlazorWindow();

            string directoryPath = @"."; // Specify the directory path
            string searchPattern = "Logs-*.txt"; // Pattern to match the file names

            try
            {
                // Get all file paths matching the pattern in the specified directory
                string[] filesToDelete = Directory.GetFiles(directoryPath, searchPattern);

                // Iterate over the file paths and delete each file
                foreach (string filePath in filesToDelete)
                {
                    File.Delete(filePath);
                    Console.WriteLine($"Deleted file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., directory not found, lack of permissions)
                Console.WriteLine($"An error occurred: {ex.Message}");
            }

            // Wait for the WPF window to initialize
            await Task.Delay(2000);
        }

        public Task DisposeAsync()
        {
            BlazorWebViewFactory.Shutdown();
            Process?.Kill();
            return Task.CompletedTask;
        }
    }
}
