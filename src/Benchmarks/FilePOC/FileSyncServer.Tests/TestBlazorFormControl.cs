// TestBlazorFormControl.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xunit;
using Xunit.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.WindowsForms;
using PeakSWC.RemoteWebView;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace WebdriverTestProject
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
        private RootComponent CreateRootComponent()
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

            Assert.Throws<Exception>(() =>
            {
                BlazorWebViewFormFactory.MainForm?.Invoke(() =>
                {
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.Id = Guid.NewGuid();
                });
            });
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
        public void TestConnectedEvent()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            AutoResetEvent threadInitialized = new AutoResetEvent(false);

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
                    Task.Delay(3000).Wait();
                    threadInitialized.Set();
                };

                webView.ReadyToConnect += (sender, e) =>
                {
                    webView.NavigateToString($"<a href='{e.Url}app/{e.Id}' target='_blank'> {e.Url}app/{e.Id}</a>");
                    Utilities.OpenUrlInBrowser($"{e.Url}app/{e.Id}");
                };

                webView.HostPage = @"wwwroot\index.html";
            });

            Assert.True(threadInitialized.WaitOne(10000));
        }

        [Fact]
        public void TestJsDownload()
        {
            var rootComponent = CreateRootComponent();
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            AutoResetEvent threadInitialized = new AutoResetEvent(false);

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
                    threadInitialized.Set();
                };

                webView.ReadyToConnect += (sender, e) =>
                {
                    webView.NavigateToString($"<a href='{e.Url}app/{e.Id}' target='_blank'> {e.Url}app/{e.Id}</a>");
                    Utilities.OpenUrlInBrowserWithDevTools($"{e.Url}app/{e.Id}");
                };

                // 800 x 1k bytes passes
                // 100 x 100k bytes passes
                Stopwatch sw = new Stopwatch();
                sw.Start();
                _output.WriteLine("Generating JS files");
                Utilities.GenJavascript(800, 1000);
                // Utilities.GenJavascript(100,100_000);
                _output.WriteLine($"Done Generating JS files in {sw.Elapsed}");
                webView.HostPage = @"wwwroot\index.html";
            });

            Assert.True(threadInitialized.WaitOne(30000));
            // Javascript is still running need a sync mechanism
            Task.Delay(TimeSpan.FromSeconds(10)).Wait();
        }
    }
}
