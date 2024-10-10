// TestBlazorWpfControl.cs
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.Wpf;
using PeakSWC.RemoteWebView;
using Xunit;
using Xunit.Abstractions;

namespace WebdriverTestProject
{
    public class TestBlazorWpfControl : IClassFixture<TestBlazorWpfControlFixture>
    {
        private readonly TestBlazorWpfControlFixture _fixture;
        private readonly ITestOutputHelper _output;

        public TestBlazorWpfControl(TestBlazorWpfControlFixture fixture, ITestOutputHelper output)
        {
            _fixture = fixture;
            _output = output;
        }

        // Helper method to create root component
        private RootComponent CreateRootComponent()
        {
            return new RootComponent
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()
            };
        }

        [Fact]
        public async Task TestSetMirrorPropertyLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    // webView.GrpcBaseUri = new Uri("https://localhost:5002");

                    webView.HostPage = @"wwwroot\index.html";
                    webView.EnableMirrors = true;
                });
            });

            _output.WriteLine($"Caught expected exception: {ex.Message}");
        }

        [Fact]
        public async Task TestGroupLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.Group = "group";
                });
            });

            _output.WriteLine($"Caught expected exception: {ex.Message}");
        }

        [Fact]
        public async Task TestMarkupLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.Markup = "markup";
                });
            });

            _output.WriteLine($"Caught expected exception: {ex.Message}");
        }

        [Fact]
        public async Task TestSetPingIntervalLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.PingIntervalSeconds = 50;
                });
            });

            _output.WriteLine($"Caught expected exception: {ex.Message}");
        }

        [Fact]
        public async Task TestSetIdPropertyLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var ex = await Assert.ThrowsAsync<Exception>(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.Id = Guid.NewGuid();
                });
            });

            _output.WriteLine($"Caught expected exception: {ex.Message}");
        }

        [Fact]
        public async Task TestGrpcBaseUriPropertyLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.ServerUri = new Uri("https://localhost:5001");
                    webView.HostPage = @"wwwroot\index.html";
                    webView.GrpcBaseUri = new Uri("https://localhost:5002");
                });
            });

            _output.WriteLine($"Caught expected exception: {ex.Message}");
        }

        [Fact]
        public async Task TestServerUriPropertyLate()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    webView.Id = Guid.NewGuid();
                    webView.HostPage = @"wwwroot\index.html";
                    webView.ServerUri = new Uri("https://localhost:5001");
                });
            });

            _output.WriteLine($"Caught expected exception: {ex.Message}");
        }

        [Fact]
        public async Task TestMirrorPropertyOnTime()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() =>
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
        public async Task TestPropertiesOnTime()
        {
            var rootComponent = CreateRootComponent();
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.NotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() =>
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
    }
}
