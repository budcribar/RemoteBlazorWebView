// BaseTestFixture.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Xunit;

namespace WebdriverTestProject
{
    public abstract class BaseTestFixture : IAsyncLifetime
    {
        public IPlaywright PlaywrightInstance { get; private set; }
        public IBrowser Browser { get; private set; }
        public IPage Page { get; private set; }
        public Process? AppProcess { get; private set; }

        protected string AppExecutablePath { get; set; }

        private const int RemoteDebuggingPort = 9222;
        private readonly TimeSpan BrowserWsTimeout = TimeSpan.FromSeconds(15); // Increased timeout for reliability

        protected BaseTestFixture() { }
        //protected BaseTestFixture (string path)
        //{
        //    AppExecutablePath = path;
        //}

        public async Task InitializeAsync()
        {
            // Start the application with remote debugging enabled
            AppProcess = StartApplication();

            // Initialize Playwright
            PlaywrightInstance = await Playwright.CreateAsync();

            // Retrieve the WebSocket Debugger URL, waiting until it's available
            var browserWsUrl = await GetBrowserWebSocketUrlAsync(RemoteDebuggingPort, BrowserWsTimeout);
            if (string.IsNullOrEmpty(browserWsUrl))
            {
                throw new InvalidOperationException("Failed to retrieve the WebSocket URL for Playwright to connect.");
            }

            // Connect Playwright to the existing WebView2 instance via CDP
            Browser = await PlaywrightInstance.Chromium.ConnectOverCDPAsync(browserWsUrl);

            // Access existing contexts and pages
            var contexts = Browser.Contexts.ToList();
            if (contexts.Count == 0)
            {
                throw new InvalidOperationException("No browser contexts found in the connected WebView2 instance.");
            }

            var pages = contexts[0].Pages.ToList();
            if (pages.Count == 0)
            {
                throw new InvalidOperationException("No pages found in the first browser context.");
            }

            Page = pages[0];

            // Optional: Verify that the page has loaded expected content
            // await Page.WaitForSelectorAsync("selector-for-some-element");
        }

        public async Task DisposeAsync()
        {
            // Close Playwright browser
            await Browser.CloseAsync();
            PlaywrightInstance.Dispose();

            // Terminate the application process
            if (AppProcess != null && !AppProcess.HasExited)
            {
                AppProcess.Kill();
                AppProcess.WaitForExit();
            }
        }

        // Method to start the application with remote debugging enabled
        private Process StartApplication()
        {
            if (!File.Exists(AppExecutablePath))
            {
                throw new FileNotFoundException($"Application executable not found at path: {AppExecutablePath}");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(AppExecutablePath),
                Arguments = "", // Add any necessary command-line arguments
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetFullPath(Path.GetDirectoryName(AppExecutablePath)??"")
            };

            // Set the environment variable for remote debugging
            startInfo.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = "--remote-debugging-port=9222";

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start the application process.");
            }

            return process;
        }

        // Method to retrieve the WebSocket Debugger URL
        private async Task<string?> GetBrowserWebSocketUrlAsync(int port, TimeSpan timeout)
        {
            var httpClient = new HttpClient();
            var startTime = DateTime.Now;
            string url = $"http://localhost:{port}/json/version";

            while (DateTime.Now - startTime < timeout)
            {
                try
                {
                    var response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using JsonDocument doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out JsonElement wsUrlElement))
                        {
                            return wsUrlElement.GetString();
                        }
                    }
                }
                catch
                {
                    // Ignore exceptions and retry
                }

                await Task.Delay(100); // Reduced delay between retries for faster responsiveness
            }

            return null;
        }

    }
}
