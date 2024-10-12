// RemoteBlazorWpfFixture.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;
using Xunit;
using PeakSWC.RemoteWebView;

namespace WebdriverTestProject
{
    public class RemoteBlazorWpfFixture : IAsyncLifetime, IDisposable
    {
        public IPlaywright PlaywrightInstance { get; private set; }
        public IBrowser Browser { get; private set; }
        public List<IPage> Pages { get; private set; } = new();
        public string Url { get; } = @"https://localhost:5001/";
        public string GrpcUrl { get; private set; } = @"https://localhost:5001/";
        public GrpcChannel? Channel { get; private set; }
        public List<string> Ids { get; private set; } = new();
        public Process? ServerProcess { get; private set; }
        public List<Process> Clients { get; private set; } = new();
        public int NumLoopsWaitingForPageLoad { get; } = 200;

        public RemoteBlazorWpfFixture()
        {
            // Default constructor without ITestOutputHelper
        }

        public virtual Process CreateClient(string url, string id)
        {
            return Utilities.StartRemoteBlazorWpfApp(url, id);
        }

        public virtual void KillClient()
        {
            Utilities.KillRemoteBlazorWpfApp();
        }

        public virtual int CountClients()
        {
            return Utilities.CountRemoteBlazorWpfApp();
        }

        public virtual Process StartServer()
        {
            return Utilities.StartServer();
        }

        public async Task InitializeAsync()
        {
            // Initialize Playwright
            PlaywrightInstance = await Playwright.CreateAsync();
            Browser = await PlaywrightInstance.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false, // Set to true for headless mode
                Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
            });

            // Initialize gRPC channel
            string? envVarValue = Environment.GetEnvironmentVariable("Rust");
            if (envVarValue != null)
            {
                GrpcUrl = @"https://localhost:5002/";
            }

            Channel = GrpcChannel.ForAddress(GrpcUrl);
        }

        public async Task DisposeAsync()
        {
            // Cleanup Playwright resources
            if (Browser != null)
            {
                await Browser.CloseAsync();
            }

            PlaywrightInstance?.Dispose();

            // Terminate server and client processes
            Cleanup();

            await Task.CompletedTask;
        }

        public void Dispose()
        {
            // Ensure cleanup is called
            Cleanup();
        }

        public async Task Startup(int numClients)
        {
            if (Pages.Count != 0)
            {
                throw new InvalidOperationException("Pages have not been cleared out at startup.");
            }

            KillClient();

            ServerProcess = StartServer();

            Clients = new List<Process>();

            Stopwatch sw = new();
            sw.Start();
            Ids = new List<string>();
            for (int i = 0; i < numClients; i++)
            {
                Ids.Add(Guid.NewGuid().ToString());
                Clients.Add(CreateClient(Url, Ids[i]));
            }

            // Optionally wait for clients to connect
            // WaitForClientToConnect(numClients);

            Console.WriteLine($"Clients started in {sw.Elapsed}");

            // Initialize Playwright pages
            sw.Restart();
            for (int i = 0; i < numClients; i++)
            {
                var context = await Browser.NewContextAsync(new BrowserNewContextOptions
                {
                    IgnoreHTTPSErrors = true,
                    ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
                });

                var page = await context.NewPageAsync();
                Pages.Add(page);
            }

            Console.WriteLine($"Browsers started in {sw.Elapsed}");

            // Navigate to the home page for each client
            for (int i = 0; i < numClients; i++)
            {
                await Pages[i].GotoAsync(Url + $"app/{Ids[i]}");
            }

            // Wait for navigation
            await Task.Delay(3000);
        }

        protected void WaitForClientToConnect(int num)
        {
            if (Channel == null)
            {
                throw new InvalidOperationException("gRPC Channel is not initialized.");
            }

            var client = new WebViewIPC.WebViewIPCClient(Channel);
            int count = 0;
            do
            {
                Ids = client.GetIds(new Empty()).Responses.ToList();
                Thread.Sleep(100);
                count++;
                Assert.True(count < 200, $"Timed out waiting to start {num} clients found {Ids.Count}");
            } while (Ids.Count != num);
        }

        public void Cleanup()
        {
            try
            {
                ServerProcess?.Kill();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error killing server process: {ex.Message}");
            }

            try
            {
                Clients.ForEach(x => x.Kill());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error killing client processes: {ex.Message}");
            }

            try
            {
                foreach (var page in Pages)
                {
                    page.CloseAsync().Wait();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing Playwright pages: {ex.Message}");
            }

            Pages.Clear();
        }

        public async virtual Task TestRefresh(int numClients, int numRefreshes)
        {
            await Startup(numClients);

            Stopwatch sw = new();
            sw.Start();

            Assert.Equal(numClients, Pages.Count);

            Console.WriteLine($"Navigate home in {sw.Elapsed}");

            sw.Restart();

            for (int k = 0; k < numRefreshes; k++)
            {
                // Click on the "Counter" link for each page
                for (int i = 0; i < numClients; i++)
                {
                    var linkLocator = Pages[i].Locator("role=link[name='Counter']");
                    await Expect(linkLocator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
                    await linkLocator.ClickAsync();

                    // Wait for the header to appear, indicating navigation
                    var headerLocator = Pages[i].Locator("role=heading[name='Counter']");
                    await headerLocator.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = 10000
                    });
                }

                Console.WriteLine($"Navigate to counter in {sw.Elapsed}");
                sw.Restart();

                // Refresh each page and wait for the "Counter" link to reappear
                for (int i = 0; i < numClients; i++)
                {
                    await Pages[i].ReloadAsync();
                    await Task.Delay(1000); // Delay to prevent WebView2 from crashing

                    // Wait for the "Counter" link to be visible after reload instead of fixed delay
                    var linkLocator = Pages[i].Locator("role=link[name='Counter']");
                    await Expect(linkLocator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
                }

                // Click on the "Counter" link again after refresh
                for (int i = 0; i < numClients; i++)
                {
                    var linkLocator = Pages[i].Locator("role=link[name='Counter']");
                    await Expect(linkLocator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });
                    await linkLocator.ClickAsync();

                    // Wait for the header to appear again, indicating navigation
                    var headerLocator = Pages[i].Locator("role=heading[name='Counter']");
                    await headerLocator.WaitForAsync(new LocatorWaitForOptions
                    {
                        State = WaitForSelectorState.Visible,
                        Timeout = 10000
                    });
                }

                // Verify that the "Counter" element exists by asserting it's visible
                for (int i = 0; i < numClients; i++)
                {
                    var counterLocator = Pages[i].Locator("role=heading[name='Counter']");
                    await Expect(counterLocator).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 10000 });

                    // Optional: Additional assertion using xUnit
                    Assert.True(await counterLocator.IsVisibleAsync(), $"Counter element should be visible on client {i + 1}");
                }

                Console.WriteLine($"Completed refresh cycle {k + 1} in {sw.Elapsed}");
            }
        }


        public virtual async Task TestClient(int num)
        {
            await Startup(num);

            Stopwatch sw = new();
            sw.Start();

            Assert.Equal(num, Pages.Count);

            Console.WriteLine($"Navigate home in {sw.Elapsed}");

            sw.Restart();

            // Click on the "Counter" link for each page
            for (int i = 0; i < num; i++)
            {
                var linkLocator = Pages[i].Locator("text=Counter");
                await linkLocator.ClickAsync();

                // Wait for the header to appear, indicating navigation
                var headerLocator = Pages[i].Locator("h1:text('Counter')");
                await headerLocator.WaitForAsync(new LocatorWaitForOptions
                {
                    State = WaitForSelectorState.Visible,
                    Timeout = 10000
                });
            }

            Console.WriteLine($"Navigate to counter in {sw.Elapsed}");

            List<ILocator> buttons = new();
            List<ILocator> paragraphs = new();

            for (int i = 0; i < num; i++)
            {
                // Locate the increment button
                var buttonLocator = Pages[i].Locator(".btn");
                await buttonLocator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

                Assert.True(await buttonLocator.IsVisibleAsync(), $"Button should be visible on client {i + 1}");
                buttons.Add(buttonLocator);

                // Locate the paragraph displaying the count
                var paragraphLocator = Pages[i].Locator("p");
                await paragraphLocator.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

                Assert.True(await paragraphLocator.IsVisibleAsync(), $"Paragraph should be visible on client {i + 1}");
                paragraphs.Add(paragraphLocator);
            }

            sw.Restart();
            int numClicks = 10;
            for (int i = 0; i < numClicks; i++)
            {
                for (int j = 0; j < num; j++)
                {
                    await buttons[j].ClickAsync();
                }
            }

            Console.WriteLine($"Click {numClicks} times in {sw.Elapsed}");

            int passCount = 0;
            for (int i = 0; i < num; i++)
            {
                // Wait for the paragraph to contain the expected number of clicks
                await Assertions.Expect(paragraphs[i]).ToContainTextAsync($"{numClicks}", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });

                var res = await paragraphs[i].InnerTextAsync();
                if (res.Contains($"{numClicks}")) passCount++;
            }
            Assert.Equal(num, passCount);
        }

    }
}
