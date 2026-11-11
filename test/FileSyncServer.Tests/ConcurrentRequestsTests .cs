using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using FileSyncServer;
using FluentAssertions;
using Microsoft.Playwright;
using FileSyncServer.Tests;
using Xunit;

namespace FileSyncServer.Tests
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
                    using var response = await client.GetAsync($"/{clientId}/{fileName}");
                    var content = response.StatusCode == HttpStatusCode.OK ? await response.Content.ReadAsStringAsync() : string.Empty;
                    return (fileName, response.StatusCode, content);
                }));
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            foreach (var (FileName, Status, Content) in results)
            {
                Status.Should().Be(HttpStatusCode.OK, $"Expected status code 200 for existing file '{FileName}'.");
                Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(FileName)}.txt", $"File content for '{FileName}' should match the expected content.");
            }
        }

        [Fact]
        public async Task MultipleConcurrentFileRequestsWithSameFile_ReturnsCorrectResponses()
        {
            // Arrange
            var fileNames = new List<string> { "test1.txt", "test2.txt", "test3.txt" };
            var clientCachePath = Path.Combine(Directory.GetCurrentDirectory(), "client_cache");
            var existingFiles = fileNames
                .Where(f => File.Exists(Path.Combine(clientCachePath, f)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

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
                        using var response = await client.GetAsync($"/{clientId}/{fileName}");
                        var content = await response.Content.ReadAsStringAsync();
                        return (fileName, response.StatusCode, content);
                    }));
                }
            }

            var results = await Task.WhenAll(tasks);

            // Assert
            foreach (var (FileName, Status, Content) in results)
            {
                if (existingFiles.Contains(FileName))
                {
                    Status.Should().Be(HttpStatusCode.OK, $"Expected status code 200 for existing file '{FileName}', got body: '{Content}'.");
                    Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(FileName)}.txt", $"File content for '{FileName}' should match the expected content.");
                }
                else
                {
                    Status.Should().Be(HttpStatusCode.NotFound, $"Expected status code 404 for non-existing file '{FileName}'.");
                    Content.Should().Be($"There is no content at {FileName}", $"Response content for '{FileName}' should match the missing-file body.");
                }
            }
        }

        [Fact]
        public async Task MultipleConcurrentFileRequestsWithSameFile_BrowserAndNoClientCaching()
        {
            await Utilities.SetClientCache(false);
            try
            {
                (await Utilities.GetClientCache()).Should().BeFalse();
                await AssertBrowserSwarmAsync(verifyWarmCache: false);
            }
            finally
            {
                await Utilities.SetClientCache(false);
            }
        }

        [Fact]
        public async Task MultipleConcurrentFileRequestsWithSameFile_BrowserAndClientCaching()
        {
            await Utilities.SetClientCache(true);
            try
            {
                (await Utilities.GetClientCache()).Should().BeTrue();
                await AssertBrowserSwarmAsync(verifyWarmCache: true);
            }
            finally
            {
                await Utilities.SetClientCache(false);
            }
        }

        // 300 browser clients at once: 100 rounds of test1..3.txt, every request from
        // its own browser context. Each context has its own connection pool and HTTP
        // cache, so the relay sees 300 separate TLS connections arriving together.
        // A shared context would multiplex them over one HTTP/2 connection (at most
        // 100 streams in Kestrel) and serve repeats from the browser cache.
        // The contexts are spread over SwarmBrowsers Chromium processes: 300 contexts
        // in a single process failed with net::ERR_INSUFFICIENT_RESOURCES.
        private const int SwarmRounds = 100;
        private const int SwarmBrowsers = 10;

        private async Task AssertBrowserSwarmAsync(bool verifyWarmCache = false)
        {
            var fileNames = new List<string> { "test1.txt", "test2.txt", "test3.txt" };
            var clientId = _clientFixture.ClientId;

            using var playwright = await Playwright.CreateAsync();
            var browsers = new List<IBrowser>(SwarmBrowsers);
            try
            {
                for (int i = 0; i < SwarmBrowsers; i++)
                {
                    browsers.Add(await playwright.Chromium.LaunchAsync(HeadlessChromiumOptions()));
                }

                int warmCount = verifyWarmCache ? SwarmBrowsers * fileNames.Count : 0;
                var warmSessions = new (IBrowserContext? Context, IPage? Page, string FileName)[warmCount];
                try
                {
                    var tasks = new List<Task<(string FileName, int Status, string Content)>>();
                    int n = 0;
                    for (int i = 0; i < SwarmRounds; i++)
                    {
                        foreach (var fileName in fileNames)
                        {
                            int clientIndex = n++;
                            var browser = browsers[clientIndex % SwarmBrowsers];
                            tasks.Add(Task.Run(async () =>
                            {
                                if (clientIndex < warmCount)
                                {
                                    var context = await browser.NewContextAsync();
                                    warmSessions[clientIndex] = (context, null, fileName);
                                    var page = await context.NewPageAsync();
                                    warmSessions[clientIndex] = (context, page, fileName);

                                    var response = await page.GotoAsync($"{Utilities.BASEURL}/{clientId}/{fileName}", new PageGotoOptions
                                    {
                                        WaitUntil = WaitUntilState.NetworkIdle // Wait for all network activity to finish
                                    });
                                    var content = await (response?.TextAsync() ?? Task.FromResult(string.Empty));
                                    return (fileName, response?.Status ?? -1, content);
                                }
                                else
                                {
                                    await using var context = await browser.NewContextAsync();
                                    var page = await context.NewPageAsync();
                                    var response = await page.GotoAsync($"{Utilities.BASEURL}/{clientId}/{fileName}", new PageGotoOptions
                                    {
                                        WaitUntil = WaitUntilState.NetworkIdle // Wait for all network activity to finish
                                    });
                                    var content = await (response?.TextAsync() ?? Task.FromResult(string.Empty));
                                    return (fileName, response?.Status ?? -1, content);
                                }
                            }));
                        }
                    }

                    var results = await Task.WhenAll(tasks);

                    foreach (var (FileName, Status, Content) in results)
                    {
                        Status.Should().Be((int)HttpStatusCode.OK, $"Expected HTTP 200 status on initial load for '{FileName}'.");
                        Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(FileName)}.txt", $"File content for '{FileName}' should match the expected content.");
                    }

                    if (verifyWarmCache)
                    {
                        var warmTasks = warmSessions.Select(session => Task.Run(async () =>
                        {
                            var page = session.Page!;
                            var secondResponse = await page.GotoAsync($"{Utilities.BASEURL}/{clientId}/{session.FileName}", new PageGotoOptions
                            {
                                WaitUntil = WaitUntilState.NetworkIdle
                            });
                            int secondStatus = secondResponse?.Status ?? -1;
                            bool fromCache = secondResponse != null && await page.EvaluateAsync<bool>(
                                "() => { const nav = performance.getEntriesByType('navigation')[0]; return nav !== undefined && nav.transferSize === 0; }");
                            string secondContent = secondResponse != null ? await secondResponse.TextAsync() : string.Empty;
                            if (string.IsNullOrEmpty(secondContent))
                            {
                                secondContent = await page.InnerTextAsync("body");
                            }
                            return (session.FileName, secondStatus, fromCache, secondContent);
                        })).ToList();

                        var warmResults = await Task.WhenAll(warmTasks);
                        foreach (var (FileName, SecondStatus, FromCache, SecondContent) in warmResults)
                        {
                            bool servedFromWarmCache =
                                SecondStatus == (int)HttpStatusCode.NotModified ||
                                (SecondStatus == (int)HttpStatusCode.OK && FromCache);
                            servedFromWarmCache.Should().BeTrue(
                                $"Expected warm cache hit (200 from browser cache or 304) for '{FileName}', but got status {SecondStatus} (fromCache={FromCache}).");
                            SecondContent.Should().Contain($"This is {Path.GetFileNameWithoutExtension(FileName)}.txt",
                                $"Cached file content for '{FileName}' should match the expected content.");
                        }
                    }
                }
                finally
                {
                    foreach (var session in warmSessions)
                    {
                        if (session.Context != null)
                        {
                            try
                            {
                                await session.Context.DisposeAsync();
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error disposing warm browser context: {ex.Message}");
                            }
                        }
                    }
                }
            }
            finally
            {
                foreach (var browser in browsers)
                {
                    try
                    {
                        await browser.DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error disposing browser: {ex.Message}");
                    }
                }
            }
        }

        // Chromium's Windows TCP port randomization fails connect() with WSAENOBUFS
        // (net::ERR_NO_BUFFER_SPACE) even when ephemeral ports are still free.
        // Playwright disables TcpPortRandomizationWin for that reason
        // (https://github.com/microsoft/playwright/pull/43605). A later
        // --disable-features switch replaces Playwright 1.63's switch, so this
        // list is that version's set plus TcpPortRandomizationWin.
        internal static BrowserTypeLaunchOptions HeadlessChromiumOptions() => new()
        {
            Headless = true,
            Args =
            [
                "--disable-features=AvoidUnnecessaryBeforeUnloadCheckSync,DestroyProfileOnBrowserClose,DialMediaRouteProvider,GlobalMediaControls,HttpsUpgrades,LensOverlay,MediaRouter,PaintHolding,ThirdPartyStoragePartitioning,BlockOriginHeaderModificationOnRedirect,Translate,AutoDeElevate,OptimizationHints,msForceBrowserSignIn,msEdgeUpdateLaunchServicesPreferredVersion,TcpPortRandomizationWin"
            ]
        };
    }
}
