using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FileSyncServer;
using FluentAssertions;
using WebdriverTestProject;
using Xunit;

namespace Server
{
    [Collection("Server collection")]
    public class FileFetchingTests
    {
        private readonly ServerFixture _serverFixture;
        private readonly ClientFixture _clientFixture;

        public FileFetchingTests(ServerFixture serverFixture, ClientFixture clientFixture)
        {
            _serverFixture = serverFixture;
            _clientFixture = clientFixture;

            // Determine the path to the client executable
            var testDirectory = Directory.GetCurrentDirectory();

            // Ensure test files exist in the client's cache directory
            var clientCachePath = Path.Combine(testDirectory, "client_cache");
            Directory.CreateDirectory(clientCachePath);

            for (int i = 1; i <= 3; i++)
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

        [Theory]
        [InlineData("test1.txt")]
        [InlineData("test2.txt")]
        [InlineData("test3.txt")]
        public async Task GetExistingFile_ReturnsFileContent(string fileName)
        {
            // Arrange
            var client = Utilities.Client();

            var clientId = _clientFixture.ClientId; // Retrieve the clientId from ClientFixture

            // Act
            var response = await client.GetAsync($"/{clientId}/{fileName}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"Expected status code 200 for existing file '{fileName}'.");
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain($"This is {fileName}", $"File content for '{fileName}' should match the expected content.");
        }

        [Theory]
        [InlineData("nonexistent1.txt")]
        [InlineData("nonexistent2.txt")]
        [InlineData("unknownfile.txt")]
        public async Task GetNonExistingFile_ReturnsNotFound(string fileName)
        {
            // Arrange
            var client = Utilities.Client();

            var clientId = _clientFixture.ClientId; // Retrieve the clientId from ClientFixture

            // Act
            var response = await client.GetAsync($"/{clientId}/{fileName}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.NotFound, $"Expected status code 404 for non-existing file '{fileName}'.");
            var content = await response.Content.ReadAsStringAsync();
            content.Should().Contain("File not found.", $"Response content for '{fileName}' should indicate that the file was not found.");
        }

        [Fact]
        public async Task GetLargeFile_ReturnsFileContent()
        {
            // Arrange
            var fileName = "largeemptyfile.txt";
            var testDirectory = Directory.GetCurrentDirectory();
            var clientCachePath = Path.Combine(testDirectory, "client_cache");
            var largeFilePath = Path.Combine(clientCachePath, fileName);

            // Ensure the large file exists (e.g., 100MB)
            LargeFileSetup.EnsureLargeFileExists(largeFilePath, 100 * 1024 * 1024); // 100MB

            var client = Utilities.Client();

            var clientId = _clientFixture.ClientId; // Retrieve the clientId from ClientFixture

            // Act
            var response = await client.GetAsync($"/{clientId}/{fileName}");

            // Assert
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"Expected status code 200 for large file '{fileName}'.");
            response.Content.Headers.ContentLength.Should().Be(100 * 1024 * 1024, $"Expected content length of 100MB for '{fileName}'.");

            using var contentStream = await response.Content.ReadAsStreamAsync();
            using var ms = new MemoryStream();
            await contentStream.CopyToAsync(ms);
            ms.Length.Should().Be(100 * 1024 * 1024, $"Stream length should be 100MB for '{fileName}'.");
        }
    }
}
