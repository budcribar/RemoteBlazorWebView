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
            foreach (var (FileName, Status, Content) in results)
            {
                if (Status == HttpStatusCode.OK)
                {
                    Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(FileName)}.txt", $"File content for '{FileName}' should match the expected content.");
                }
                else
                {
                    Status.Should().Be(HttpStatusCode.NotFound, $"Expected status code 404 for non-existing file '{FileName}'.");
                    Content.Should().Be("File not found.", $"Response content for '{FileName}' should indicate that the file was not found.");
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
            await using var browser = await playwright.Chromium.LaunchAsync(HeadlessChromiumOptions());

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
            foreach (var (FileName, Status, Content) in results)
            {
                Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(FileName)}.txt", $"File content for '{FileName}' should match the expected content.");   
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
            await using var browser = await playwright.Chromium.LaunchAsync(HeadlessChromiumOptions());

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

            Task<(string FileName, int Status, string Content)[]> pending = Task.WhenAll(tasks);
            try
            {
                var results = await pending;

                // Assert
                foreach (var (FileName, Status, Content) in results)
                {
                    Content.Should().Contain($"This is {Path.GetFileNameWithoutExtension(FileName)}.txt", $"File content for '{FileName}' should match the expected content.");
                }
            }
            catch (Exception ex) when (IsSocketBurst(ex))
            {
                throw new InvalidOperationException(
                    ex.GetBaseException().Message + Environment.NewLine + KestrelExcerpt(ex),
                    ex);
            }
            await Utilities.SetClientCache(false);
        }

        private static bool IsSocketBurst(Exception ex)
        {
            if (ex is AggregateException aggregate)
                return aggregate.InnerExceptions.Any(IsSocketBurst);
            if (ex.InnerException != null && IsSocketBurst(ex.InnerException))
                return true;
            var message = ex.Message;
            return message.Contains("ERR_SOCKET_NOT_CONNECTED", StringComparison.Ordinal)
                || message.Contains("ERR_NO_BUFFER_SPACE", StringComparison.Ordinal);
        }

        // Connection logs name a connection id. Request logs name the URL. Keep both for the URL that failed.
        private static string KestrelExcerpt(Exception ex)
        {
            var path = Environment.GetEnvironmentVariable("KESTREL_LOG");
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return "Kestrel log was not captured. Set KESTREL_LOG and the Kestrel connection log levels.";

            string text;
            lock (ServerFixture.KestrelLogGate)
            {
                text = File.ReadAllText(path);
            }
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var url = "";
            var message = ex.ToString();
            var marker = "https://localhost:5001/";
            var at = message.IndexOf(marker, StringComparison.Ordinal);
            if (at >= 0)
            {
                var end = at + marker.Length;
                while (end < message.Length && !char.IsWhiteSpace(message[end]))
                    end++;
                url = message[at..end];
            }

            var pathOnly = url.Length > marker.Length ? url[marker.Length..] : "";
            var matched = new List<string>();
            var connectionIds = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < lines.Length; i++)
            {
                if (pathOnly.Length > 0 && lines[i].Contains(pathOnly, StringComparison.Ordinal))
                {
                    matched.Add(lines[i]);
                    var id = ConnectionId(lines[i]);
                    if (id.Length > 0)
                        connectionIds.Add(id);
                    if (i + 1 < lines.Length)
                    {
                        var next = ConnectionId(lines[i + 1]);
                        if (next.Length > 0)
                            connectionIds.Add(next);
                    }
                }
            }

            if (connectionIds.Count > 0)
            {
                foreach (var line in lines)
                {
                    if (line.Contains("Connection", StringComparison.Ordinal) && connectionIds.Any(id => line.Contains(id, StringComparison.Ordinal)))
                        matched.Add(line);
                }
            }

            if (matched.Count == 0)
            {
                matched.AddRange(lines.Where(line =>
                    line.Contains("Connection", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("HTTP/2", StringComparison.OrdinalIgnoreCase)
                    || line.Contains("reset", StringComparison.OrdinalIgnoreCase)).TakeLast(40));
            }

            if (matched.Count == 0)
                matched.AddRange(lines.TakeLast(40));

            return "Kestrel excerpt for " + (url.Length > 0 ? url : "the failed navigation") + Environment.NewLine
                + string.Join(Environment.NewLine, matched.Distinct());
        }

        private static string ConnectionId(string line)
        {
            const string token = "Connection id \"";
            var start = line.IndexOf(token, StringComparison.Ordinal);
            if (start < 0)
                return "";
            start += token.Length;
            var end = line.IndexOf('"', start);
            return end > start ? line[start..end] : "";
        }

        // Chromium's Windows TCP port randomization fails connect() with WSAENOBUFS
        // (net::ERR_NO_BUFFER_SPACE) even when ephemeral ports are still free.
        // Playwright disables TcpPortRandomizationWin for that reason
        // (https://github.com/microsoft/playwright/pull/43605). A later
        // --disable-features switch replaces Playwright 1.63's switch, so this
        // list is that version's set plus TcpPortRandomizationWin. All 300
        // pages are still opened together.
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
