// TestBlazorFormControlFixture.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grpc.Net.Client;
using Google.Protobuf.WellKnownTypes;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.WindowsForms;
using PeakSWC.RemoteWebView;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace WebdriverTestProject
{
    public class TestBlazorFormControlFixture : IAsyncLifetime
    {
        public Process? Process { get; private set; }
        public Form? MainForm { get; private set; }

        public async Task InitializeAsync()
        {
            string grpcUrl = @"https://localhost:5001/";
            GrpcChannel? channel;
            string? envVarValue = Environment.GetEnvironmentVariable(variable: "Rust");
            if (envVarValue != null)
                grpcUrl = @"https://localhost:5002/";

            channel = GrpcChannel.ForAddress(grpcUrl);
            Process = Utilities.StartServer();

            for (int i = 0; i < 10; i++)
            {
                // Wait for server to spin up
                try
                {
                    var ids = new WebViewIPC.WebViewIPCClient(channel).GetIds(new Empty());
                    Assert.Equal(0, ids.Responses.Count); // Using xUnit's Assert
                    break;
                }
                catch (Exception)
                {
                    // Wait and retry
                }
                await Task.Delay(1000);
            }

            MainForm = BlazorWebViewFormFactory.CreateBlazorWindow();

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
        }

        public void Dispose()
        {
            BlazorWebViewFormFactory.Shutdown();
            Process?.Kill();
        }

        // Synchronous Dispose for IAsyncLifetime
        public Task DisposeAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }
    }
}
