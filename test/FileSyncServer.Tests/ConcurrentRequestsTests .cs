using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FileSyncServer;
using FluentAssertions;
using Microsoft.Playwright;
using WebdriverTestProject;
using Xunit;

namespace Server
{
    [Collection("Server collection")]
    public class ConcurrentRequestsTests
    {
        private readonly ServerFixture _serverFixture;
        private readonly ClientFixture _clientFixture;

        public ConcurrentRequestsTests(ServerFixture serverFixture, ClientFixture clientFixture)
        {
            _serverFixture = serverFixture;
            _clientFixture = clientFixture;

            // Determine the path to the client's cache directory
            var testDirectory = Directory.GetCurrentDirectory();

            // Ensure test files exist in the client's cache directory under the specific clientId
            var clientCachePath = Path.Combine(testDirectory, "client_cache");
            Directory.CreateDirectory(clientCachePath);

            // Create 100 test files specific to the clientId
            for (int i = 1; i <= 100; i++) // 100 test files
            {
                var filePath = Path.Combine(clientCachePath, $"test{i}.txt");
                if (!File.Exists(filePath))
                {
                    File.WriteAllText(filePath, $"This is test{i}.txt created on {DateTime.Now}.");
                }
            }

            // Verify that both Client and Server processes are running
            Process.GetProcessesByName("Client").Length.Should().BeGreaterThan(0, "Client process should be running.");
            Process.GetProcessesByName("RemoteWebViewService").Length.Should().BeGreaterThan(0, "Server process should be running.");
        }

        [Fact]
        public async Task MultipleConcurrentFileRequests_ReturnsCorrectResponses()
        {
            // Arrange
            var fileNames = new List<string>();
            for (int i = 1; i <= 100; i++)
            {
                fileNames.Add($"test{i}.txt");
            }

            var clientId = _clientFixture.ClientId; // Retrieve the clientId from ClientFixture

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };

            using var client = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://localhost:5001"),
                Timeout = TimeSpan.FromMinutes(5) // Adjust as necessary
            };
            client.DefaultRequestVersion = HttpVersion.Version11;

            var tasks = new List<Task<(string FileName, HttpStatusCode Status, string Content)>>();

            // Act
            foreach (var fileName in fileNames)
            {
                tasks.Add(Task.Run(async () =>
                {
                    var response = await client.GetAsync($"/{clientId}/{fileName}");
                    var content = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync() : string.Empty;
                    return (fileName, response.StatusCode, content);
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            foreach (var result in results)
            {
                result.Status.Should().Be(HttpStatusCode.OK, $"Expected status code 200 for existing file '{result.FileName}'.");
                result.Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(result.FileName)}.txt", $"File content for '{result.FileName}' should match the expected content.");
            }
        }

        [Fact]
        public async Task MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses()
        {
            // Arrange
            var fileNames = new List<string> { "test1.txt", "test2.txt", "test3.txt" };
            
            var clientId = _clientFixture.ClientId; // Retrieve the clientId from ClientFixture

            using var client = Utilities.Client();   

            var tasks = new List<Task<(string FileName, HttpStatusCode Status, string Content)>>();

            // Act
            //for (int i = 0; i < 1667; i++) // 5,000 concurrent requests max; much past this hangs parallel client reads
            for (int i = 0; i < 166; i++)
            {
                foreach (var fileName in fileNames)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var response = await client.GetAsync($"/{clientId}/{fileName}");
                        var content = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync() : string.Empty;
                        return (fileName, response.StatusCode, content);
                    }));
                }
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            foreach (var result in results)
            {
                if (result.Status == HttpStatusCode.OK)
                {
                    result.Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(result.FileName)}.txt", $"File content for '{result.FileName}' should match the expected content.");
                }
                else
                {
                    result.Status.Should().Be(HttpStatusCode.NotFound, $"Expected status code 404 for non-existing file '{result.FileName}'.");
                    result.Content.Should().Be("File not found.", $"Response content for '{result.FileName}' should indicate that the file was not found.");
                }
            }
        }

        [Fact]
        public async Task MultipleConcurrentFileRequestsWithSameFile_BrowserAndNoClientCaching()
        {
            // Enable client-side caching
            await Utilities.SetClientCache(false);
            // (await Utilities.GetClientCache()).Should().BeTrue
            (await Utilities.GetClientCache()).Should().BeFalse();
            // Arrange
            var fileNames = new List<string> { "test1.txt", "test2.txt", "test3.txt" };

            var clientId = _clientFixture.ClientId; // Retrieve the clientId from ClientFixture

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true // Run in headless mode
            });

            var tasks = new List<Task<(string FileName, int Status, string Content)>>();

            // Act
            for (int i = 0; i < 100; i++) // 5,000 concurrent requests max; much past this hangs
            {
                foreach (var fileName in fileNames)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var page = await browser.NewPageAsync();
                        var response = await page.GotoAsync($"{Utilities.BASE_URL}/{clientId}/{fileName}",new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.NetworkIdle // Wait for all network activity to finish
                        });
                        var content = await (response?.TextAsync() ?? Task.FromResult<string>(""));
                        return (fileName, response?.Status ?? -1, content);
                    }));
                }
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            foreach (var result in results)
            {
                result.Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(result.FileName)}.txt", $"File content for '{result.FileName}' should match the expected content.");   
            }
            await Utilities.SetClientCache(false);
        }

        [Fact]
        public async Task MultipleConcurrentFileRequestsWithSameFile_BrowserAndClientCaching()
        {
            // Enable client-side caching
            await Utilities.SetClientCache(true);
            (await Utilities.GetClientCache()).Should().BeTrue();
          
            // Arrange
            var fileNames = new List<string> { "test1.txt", "test2.txt", "test3.txt" };

            var clientId = _clientFixture.ClientId; // Retrieve the clientId from ClientFixture

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true // Run in headless mode
            });

            var tasks = new List<Task<(string FileName, int Status, string Content)>>();

            // Act
            for (int i = 0; i < 100; i++) // 5,000 concurrent requests max; much past this hangs
            {
                foreach (var fileName in fileNames)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        var page = await browser.NewPageAsync();
                        var response = await page.GotoAsync($"{Utilities.BASE_URL}/{clientId}/{fileName}", new PageGotoOptions
                        {
                            WaitUntil = WaitUntilState.NetworkIdle // Wait for all network activity to finish
                        });
                        var content = await (response?.TextAsync() ?? Task.FromResult<string>(""));
                        return (fileName, response?.Status ?? -1, content);
                    }));
                }
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            foreach (var result in results)
            {
                result.Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(result.FileName)}.txt", $"File content for '{result.FileName}' should match the expected content.");
            }
            await Utilities.SetClientCache(false);
        }
    }
}
