// TestBlazorFormControl.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;
using Xunit.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.Playwright;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.WindowsForms;
using PeakSWC.RemoteWebView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FileSyncServer.Tests
{
    public class TestBlazorFormControl : IClassFixture<TestBlazorFormControlFixture>
    {
        private readonly TestBlazorFormControlFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TestBlazorFormControl(TestBlazorFormControlFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
            BlazorWebViewFormFactory.MainForm = _fixture.MainForm;
        }

        // Helper method to create root component
        private static RootComponent CreateRootComponent()
        {
            return new RootComponent("#app", typeof(Home), new Dictionary<string, object?>());
        }

        [Fact]
        public void TestSetMirrorPropertyLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            Assert.Throws<ArgumentException>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    // webView.GrpcBaseUri = new Uri("https://localhost:5002");

                    webView.HostPage = @"wwwroot\index.html";

                    webView.EnableMirrors = true;
                });
            });
        }

        [Fact]
        public void TestGroupLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            Assert.Throws<ArgumentException>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.Group = "group";
                });
            });
        }

        [Fact]
        public void TestMarkupLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            Assert.Throws<ArgumentException>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.Markup = "markup";
                });
            });
        }

        [Fact]
        public void TestSetPingIntervalLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            Assert.Throws<ArgumentException>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.PingIntervalSeconds = 50;
                });
            });
        }

        [Fact]
        public void TestSetIdPropertyLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() =>
            {
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
            });

            Assert.Throws<ArgumentException>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.Id = Guid.NewGuid();
                });
            });
        }

        [Fact]
        public void TestHostPageWithoutIdThrows()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var ex = Assert.Throws<Exception>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                });
            });

            Assert.Equal("Id not initialized", ex.Message);
        }

        [Fact]
        public void TestGrpcBaseUriPropertyLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            Assert.Throws<ArgumentException>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.GrpcBaseUri = new Uri("https://localhost:5002");
                });
            });
        }

        [Fact]
        public void TestServerUriPropertyLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            Assert.Throws<ArgumentException>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.HostPage = @"wwwroot\index.html";
                    webView.ServerUri = new Uri("https://localhost:5001");
                });
            });
        }

        [Fact]
        public void TestMirrorPropertyOnTime()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() =>
            {
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new Uri("https://localhost:5001");
                // webView.GrpcBaseUri = new Uri("https://localhost:5002");
                webView.EnableMirrors = true;
                webView.HostPage = @"wwwroot\index.html";
            });

            // If no exception is expected, the test will pass
        }

        [Fact]
        public void TestPropertiesOnTime()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() =>
            {
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new Uri("https://localhost:5001");
                webView.GrpcBaseUri = new Uri("https://localhost:5001");
                webView.EnableMirrors = true;
                webView.PingIntervalSeconds = 33;
                webView.Group = "group";
                webView.Markup = "markup";
                webView.HostPage = @"wwwroot\index.html";
            });

            // If no exception is expected, the test will pass
        }

        [Fact]
        public async Task TestConnectedEvent()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            BlazorWebViewFormFactory.MainForm?.Invoke(() =>
            {
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new Uri("https://localhost:5001");
                webView.EnableMirrors = true;

                webView.Connected += (sender, e) =>
                {
                    webView.WebView.CoreWebView2.Navigate($"{e.Url}mirror/{e.Id}");
                    var user = e.User.Length > 0 ? $"by user {e.User.Length}" : "";
                    BlazorWebViewFormFactory.MainForm.Text += $" Controlled remotely {user}from ip address {e.IpAddress}";
                    connected.TrySetResult();
                };

                webView.ReadyToConnect += (sender, e) =>
                {
                    webView.NavigateToString($"<a href='{e.Url}app/{e.Id}' target='_blank'> {e.Url}app/{e.Id}</a>");
                    Utilities.OpenUrlInBrowser($"{e.Url}app/{e.Id}");
                };

                webView.HostPage = @"wwwroot\index.html";
            });

            // 10s is only for the browser to connect. The 3s pause used to run
            // inside the handler before the signal, so a slow launch failed the
            // wait even after Connected had fired.
            await Task.WhenAny(connected.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(connected.Task.IsCompletedSuccessfully);
            await Task.Delay(3000);
        }

        [Fact]
        public async Task TestJsDownload()
        {
            const int scriptCount = 800;
            string wwwrootPath = Path.GetFullPath(@"wwwroot");
            string indexPath = Path.Combine(wwwrootPath, "index.html");
            string? originalIndexHtml = File.Exists(indexPath) ? File.ReadAllText(indexPath) : null;

            try
            {
                Utilities.DeleteGeneratedJsFiles(wwwrootPath);

                var rootComponent = CreateRootComponent();
                var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
                Assert.NotNull(webView);

                var readyTargetTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                var connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.EnableMirrors = true;

                    webView.Connected += (sender, e) =>
                    {
                        webView.WebView.CoreWebView2?.Navigate($"{e.Url}mirror/{e.Id}");
                        var user = e.User.Length > 0 ? $"by user {e.User.Length}" : "";
                        if (BlazorWebViewFormFactory.MainForm != null)
                        {
                            BlazorWebViewFormFactory.MainForm.Text += $" Controlled remotely {user}from ip address {e.IpAddress}";
                        }
                        connectedTcs.TrySetResult();
                    };

                    webView.ReadyToConnect += (sender, e) =>
                    {
                        var target = $"{e.Url}app/{e.Id}";
                        webView.NavigateToString($"<a href='{target}' target='_blank'> {target}</a>");
                        readyTargetTcs.TrySetResult(target);
                    };

                    // 800 x 1k bytes passes
                    // 100 x 100k bytes passes
                    Stopwatch sw = Stopwatch.StartNew();
                    _output.WriteLine("Generating JS files");
                    Utilities.GenJavascript(scriptCount, 1000, wwwrootPath);
                    // Utilities.GenJavascript(100,100_000);
                    _output.WriteLine($"Done Generating JS files in {sw.Elapsed}");
                    webView.HostPage = @"wwwroot\index.html";
                });

                var targetUrl = await readyTargetTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));
                _output.WriteLine($"ReadyToConnect ran. Navigating Playwright Chromium to {targetUrl}");

                using var playwright = await Playwright.CreateAsync();
                await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                });
                await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    IgnoreHTTPSErrors = true
                });
                var page = await context.NewPageAsync();

                var response = await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    Timeout = 60000,
                    WaitUntil = WaitUntilState.DOMContentLoaded
                });
                Assert.NotNull(response);

                await connectedTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));

                await page.WaitForFunctionAsync(
                    "expected => window.__jsDownloadResult && (window.__jsDownloadResult.loaded + window.__jsDownloadResult.failed) >= expected",
                    scriptCount,
                    new PageWaitForFunctionOptions { Timeout = 60000 });

                var jsResult = await page.EvaluateAsync<JsonElement>("() => window.__jsDownloadResult");
                int loaded = jsResult.GetProperty("loaded").GetInt32();
                int failed = jsResult.GetProperty("failed").GetInt32();
                _output.WriteLine($"JS download verification finished: loaded={loaded}, failed={failed}");

                Assert.Equal(0, failed);
                Assert.Equal(scriptCount, loaded);
            }
            finally
            {
                if (originalIndexHtml != null)
                {
                    Directory.CreateDirectory(wwwrootPath);
                    File.WriteAllText(indexPath, originalIndexHtml);
                }
                else if (File.Exists(indexPath))
                {
                    File.Delete(indexPath);
                }

                Utilities.DeleteGeneratedJsFiles(wwwrootPath);
            }
        }
    }
}
