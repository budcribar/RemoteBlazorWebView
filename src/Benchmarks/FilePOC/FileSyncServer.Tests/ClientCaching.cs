// FileSyncServer.Tests/FileSyncServiceImplTests.cs
using FluentAssertions;
using Microsoft.Playwright;
using System.Net;
using System.Security.Principal;

namespace FileSyncServer.Tests
{
    [Collection("Client caching collection")]
    public class ClientCachingTests : IDisposable
    {
        private readonly ServerFixture _serverFixture;
        private readonly ClientFixture _clientFixture;
        private readonly HttpClient _client = Utility.Client();
        private readonly string _testRootDirectory = Path.Combine(Directory.GetCurrentDirectory(), "client_cache");
        private readonly string _clientId = string.Empty;
        private readonly string _fileName = "testfile.txt";
        private readonly string _fileContent = "This is a test file.";
        private readonly string _filePath = string.Empty;
        // Determine the current user
        private readonly string _currentUser = WindowsIdentity.GetCurrent().Name;
        public ClientCachingTests(ServerFixture serverFixture, ClientFixture clientFixture)
        {
            _serverFixture = serverFixture;
            _clientFixture = clientFixture;
            _clientId = clientFixture.ClientId.ToString();

            // Determine the path to the client executable
            var testDirectory = Directory.GetCurrentDirectory();

            // Ensure test files exist in the client's cache directory
            var clientCachePath = Path.Combine(Directory.GetCurrentDirectory(), "client_cache");
            Directory.CreateDirectory(clientCachePath);
          
            // Create a test file with read permissions
            _filePath = Path.Combine(clientCachePath, _fileName);

            if (File.Exists(_filePath))
            {
                Utility.ModifyFilePermissions(_filePath, _currentUser, true);
                File.Delete(_filePath);
            }
               
            File.WriteAllText(_filePath, _fileContent);

            // Grant read access to the current user           
            Utility.ModifyFilePermissions(_filePath, _currentUser, true);  
        }

        [Fact]
        public async Task Client_Cache_Enabled_Should_Serve_File_After_Permission_Revoked()
        {
            // Enable client-side caching
            await Utility.SetClientCache(true);
            (await Utility.GetClientCache()).Should().BeTrue();

            // Grant file read permissions
            Utility.ModifyFilePermissions(_filePath, _currentUser, true);

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true // Run in headless mode
            });

            // Now use `browserContext` to open pages
            var page = await browser.NewPageAsync();

            // First request: Fetch file and check response
            var firstResponse = await page.GotoAsync($"{Utility.BASE_URL}/{_clientId}/{_fileName}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle // Wait for all network activity to finish
            });

            firstResponse.Should().NotBeNull();

            var fromCache = await firstResponse!.Request.Frame.EvaluateAsync<bool>("performance.getEntriesByType('navigation')[0].transferSize === 0");

            fromCache.Should().Be(false);
            firstResponse.Status.Should().Be((int)HttpStatusCode.OK);

            // Ensure the file content is correct
            string content = await firstResponse.TextAsync();
            content.Should().Be(_fileContent);

            // Revoke file read access
            Utility.ModifyFilePermissions(_filePath, _currentUser, false);

            var secondResponse = await page.GotoAsync($"{Utility.BASE_URL}/{_clientId}/{_fileName}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle
            });

            secondResponse.Should().NotBeNull();
            secondResponse!.Status.Should().Be((int)HttpStatusCode.OK);
            // Output second response status
            Console.WriteLine($"Second Response Status: {secondResponse!.Status}");          

            fromCache = await secondResponse.Request.Frame.EvaluateAsync<bool>("performance.getEntriesByType('navigation')[0].transferSize === 0");
            fromCache.Should().Be(true);

            // Optionally, verify the content is still accessible from cache (if 304, content won't change)
            string cachedContent = await secondResponse.TextAsync();
            cachedContent.Should().Be(_fileContent);

            // Restore file permissions after test
            Utility.ModifyFilePermissions(_filePath, _currentUser, true);
        }

        [Fact]
        public async Task Client_Cache_Disabled_Should_Fail_After_Permission_Revoked()
        {
            await Utility.SetClientCache(false);
            (await Utility.GetClientCache()).Should().BeFalse();

            Utility.ModifyFilePermissions(_filePath, _currentUser, true);

            // Act
            // First request: should succeed and cache the file
            var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true // Run in headless mode
            });

            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            // Step 2: Make the first request and check the response status and headers
            var firstResponse = await page.GotoAsync($"{Utility.BASE_URL}/{_clientId}/{_fileName}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle // Wait for all network activity to finish
            });

            firstResponse.Should().NotBeNull();

            firstResponse!.Status.Should().Be((int)HttpStatusCode.OK);

            string content = await firstResponse.TextAsync();
            content.Should().Be(_fileContent);

            // Revoke read access
            Utility.ModifyFilePermissions(_filePath, _currentUser, false);

            // Second request: should fail
            var secondResponse = await page.GotoAsync($"{Utility.BASE_URL}/{_clientId}/{_fileName}", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle // Wait for all network activity to finish
            });


            secondResponse!.Status.Should().Be(403);

            Utility.ModifyFilePermissions(_filePath, _currentUser, true);
        }

        public void Dispose()
        {
            // Cleanup: Delete the temporary test directory
            if (Directory.Exists(_testRootDirectory))
            {
                try
                {
                    Directory.Delete(_testRootDirectory, recursive: true);
                }
                catch (Exception)
                {
                    // Log the exception or handle accordingly
                }
            }
        }
    }
}
