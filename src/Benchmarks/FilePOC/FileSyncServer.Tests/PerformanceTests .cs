using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FileSyncServer;
using FluentAssertions;
using Xunit;

namespace Server
{
    [Collection("Performance collection")]
    public class PerformanceTests : IAsyncLifetime
    {
        private readonly ServerFixture _serverFixture;
        private readonly List<ClientFixture> _clientFixtures = new();

        public PerformanceTests(ServerFixture serverFixture)
        {
            _serverFixture = serverFixture;
        }

        // Initialize multiple clients before any tests run
        public async Task InitializeAsync()
        {
            // Kill any existing Client processes to ensure a clean start
            Utility.KillExistingProcesses("Client");

            int numberOfClients = 5; // Adjust as needed
            for (int i = 0; i < numberOfClients; i++)
            {
                var clientFixture = new ClientFixture();
                _clientFixtures.Add(clientFixture);
            }

            // Optionally, wait for all clients to complete synchronization
            //await Task.Delay(2000); // Adjust delay as necessary based on client synchronization
            await Task.CompletedTask;
        }

        // Dispose of clients after all tests run
        public async Task DisposeAsync()
        {
            foreach (var clientFixture in _clientFixtures)
            {
                clientFixture.Dispose();
                Console.WriteLine($"Disposed client with clientId: {clientFixture.ClientId}");
            }
            await Task.CompletedTask;
        }

        [Fact]
        public async Task FetchLargeFiles_PerformanceMetrics()
        {
            // Arrange
            using var clientFixture = new ClientFixture();
            var clientId = clientFixture.ClientId; // Retrieve the clientId from ClientFixture

            // Setup client cache directory with large test files
            var clientCachePath = Path.Combine(Directory.GetCurrentDirectory(), "client_cache");
            Directory.CreateDirectory(clientCachePath);

            for (int i = 1; i <= 5; i++) // 5 large test files
            {
                var fileName = $"performance_test{i}.txt";
                var filePath = Path.Combine(clientCachePath, fileName);
                if (!File.Exists(filePath))
                {
                    // Create a 50MB file
                    using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    fs.SetLength(50 * 1024 * 1024); // 50MB
                    Console.WriteLine($"Created large test file: {filePath}");
                }
                else
                {
                    Console.WriteLine($"Large test file already exists: {filePath}");
                }
            }

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://localhost:5001"),
                Timeout = TimeSpan.FromMinutes(10) // Adjust as necessary
            };

            // Initialize performance measurement
            var totalStopwatch = Stopwatch.StartNew();

            var tasks = new List<Task<(string FileName, HttpStatusCode Status, long Duration)>> ();

            // Act
            foreach (var fileName in Directory.GetFiles(clientCachePath, "*.txt"))
            {
                var shortFileName = Path.GetFileName(fileName);
                tasks.Add(Task.Run(async () =>
                {
                    var fileStopwatch = Stopwatch.StartNew();

                    var response = await client.GetAsync($"/{clientId}/{shortFileName}");
                    fileStopwatch.Stop();

                    long duration = fileStopwatch.ElapsedMilliseconds;

                    return (shortFileName, response.StatusCode, duration);
                }));
            }

            var results = await Task.WhenAll(tasks);
            totalStopwatch.Stop();

            // Collect total duration
            var totalDuration = totalStopwatch.Elapsed;

            // Assert
            foreach (var result in results)
            {
                result.Status.Should().Be(HttpStatusCode.OK, $"Expected status code 200 for file '{result.FileName}'.");
                result.Duration.Should().BeLessThan(10000, $"File '{result.FileName}' took too long to fetch ({result.Duration} ms).");
                Console.WriteLine($"Fetched '{result.FileName}' in {result.Duration} ms.");
            }

            totalDuration.TotalSeconds.Should().BeLessThan(30, $"Total time {totalDuration.TotalSeconds} seconds exceeds the threshold.");
            Console.WriteLine($"Total time for fetching large files: {totalDuration.TotalSeconds} seconds.");
        }

        [Fact]
        public async Task MultipleClientsConcurrentFileRequests_ReturnsCorrectResponses()
        {
           

            // Arrange
            var numberOfClients = _clientFixtures.Count; // Number of concurrent clients
            var clientRequests = new List<(Guid ClientId, List<string> FileNames)>();

            for (int i = 0; i < numberOfClients; i++)
            {
                var clientFixture = _clientFixtures[i];
                var clientId = clientFixture.ClientId;
                var fileNames = new List<string>();

                var clientCachePath = Path.Combine(Directory.GetCurrentDirectory(), "client_cache", i.ToString());
                Directory.CreateDirectory(clientCachePath);

                for (int j = 1; j <= 10; j++) // 10 test files per client
                {
                    var fileName = $"test{j}.txt";
                    var filePath = Path.Combine(clientCachePath, fileName);
                    if (!File.Exists(filePath))
                    {
                        File.WriteAllText(filePath, $"This is {fileName} created on {DateTime.Now}.");
                        Console.WriteLine($"Created test file: {filePath}");
                    }
                    else
                    {
                        Console.WriteLine($"Test file already exists: {filePath}");
                    }
                    fileNames.Add(Path.Combine(i.ToString(),fileName));
                }

                clientRequests.Add((clientId, fileNames));
            }

            var tasks = new List<Task<(Guid ClientId, string FileName, HttpStatusCode Status, string Content)>> ();

            var httpClient = new HttpClient { BaseAddress = new Uri("https://localhost:5001") };

            // Act
            for (int i = 0; i < numberOfClients; i++)
            {
                var clientId = clientRequests[i].ClientId;
                var fileNames = clientRequests[i].FileNames;
             
                foreach (var fileName in fileNames)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var response = await httpClient.GetAsync($"/{clientId}/{fileName}");
                        var content = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync() : string.Empty;
                        return (clientId, fileName, response.StatusCode, content);
                    }));
                }
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            foreach (var result in results)
            {
                result.Status.Should().Be(HttpStatusCode.OK, $"Expected status code 200 for file '{result.FileName}' from client '{result.ClientId}'.");
                result.Content.Should().Contain($"This is {Path.GetFileName(result.FileName)}", $"File content for '{result.FileName}' from client '{result.ClientId}' should match the expected content.");
            }
        }
    }
}
