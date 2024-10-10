// TestBlazorFormFixture.cs
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
    public class TestBlazorFormFixture : IAsyncLifetime
    {
        public IPlaywright PlaywrightInstance { get; private set; }
        public IBrowser Browser { get; private set; }
        public IPage Page { get; private set; }
        public Process? BlazorAppProcess { get; private set; }
        public string AppExecutablePath { get; private set; } = Utilities.BlazorWinFormsAppExe();

        public async Task InitializeAsync()
        {
            // Start the Blazor desktop application with remote debugging enabled
            BlazorAppProcess = StartBlazorApp();

            // Allow some time for the application to initialize and WebView2 to start
            //await Task.Delay(5000); // Adjust the delay as necessary based on app startup time

            // Initialize Playwright
            PlaywrightInstance = await Playwright.CreateAsync();

            // Retrieve the WebSocket Debugger URL
            var browserWSUrl = await GetBrowserWebSocketUrlAsync(9222, TimeSpan.FromSeconds(20));
            if (string.IsNullOrEmpty(browserWSUrl))
            {
                throw new InvalidOperationException("Failed to retrieve the WebSocket URL for Playwright to connect.");
            }

            // Connect Playwright to the existing WebView2 instance
            Browser = await PlaywrightInstance.Chromium.ConnectOverCDPAsync(browserWSUrl);

            var c = Browser.Contexts.ToList();
            Page = c.First().Pages.First();

        }

        public async Task DisposeAsync()
        {
            // Close Playwright browser
            await Browser.CloseAsync();
            PlaywrightInstance.Dispose();

            // Kill the Blazor application process
            if (BlazorAppProcess != null && !BlazorAppProcess.HasExited)
            {
                BlazorAppProcess.Kill();
                BlazorAppProcess.WaitForExit();
            }
        }

        private Process StartBlazorApp()
        {
            if (!File.Exists(AppExecutablePath))
            {
                throw new FileNotFoundException($"Blazor application executable not found at {AppExecutablePath}");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath( AppExecutablePath),
                Arguments = "", // Add any necessary arguments
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Set the environment variable for remote debugging
            startInfo.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] = "--remote-debugging-port=9222";

            var process = Process.Start(startInfo);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start the Blazor application process.");
            }

            return process;
        }

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

                await Task.Delay(100);
            }

            return null;
        }
    }
}
