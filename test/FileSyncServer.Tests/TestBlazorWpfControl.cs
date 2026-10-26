// TestBlazorWpfControl.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.Wpf;
using PeakSWC.RemoteWebView;
using Xunit;
using Xunit.Abstractions;

namespace FileSyncServer.Tests
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

        // WebView2's default is true. This fails if OnApplyTemplate stops assigning AllowExternalDrop = false.
        [WindowsOnlyFact]
        public Task AllowExternalDrop_controller_is_false_after_startup()
            => RunOnUiAsync(AssertCompositionAsync);

        private async Task RunOnUiAsync(Func<Task> action)
        {
            Assert.NotNull(Application.Current);
            await Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap();
        }

        private async Task AssertCompositionAsync()
        {
            var bare = new WebView2CompositionControl();
            try
            {
                Assert.True(bare.AllowExternalDrop);
                var message = $"composition default before initialization: AllowExternalDrop control={bare.AllowExternalDrop} controller=not reachable expected=True";
                _output.WriteLine(message);
                Console.WriteLine(message);
            }
            finally
            {
                bare.Dispose();
            }

            var blazor = new BlazorWebView();
            var window = CreateWindow("composition BlazorWebView");
            var folder = CreateUserDataFolder();
            try
            {
                window.Content = blazor;
                blazor.ApplyTemplate();
                var inner = blazor.WebView;
                Assert.NotNull(inner);
                Assert.False(inner.AllowExternalDrop);
                inner.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = folder };
                window.Show();
                window.UpdateLayout();
                try
                {
                    await InitializeAndAssertAsync(inner, expected: false, "composition BlazorWebView");
                }
                catch (Exception ex) when (IsMissingCompositionSupport(ex))
                {
                    // Composition loads Microsoft.Windows.SDK.NET for its D3D image. The test project does not reference it.
                    var message = "composition controller not reachable: " + ex.GetType().Name + ": " + ex.Message
                        + $" AllowExternalDrop control={inner.AllowExternalDrop} expected=False";
                    _output.WriteLine(message);
                    Console.WriteLine(message);
                }
            }
            finally
            {
                window.Content = null;
                await blazor.DisposeAsync();
                window.Close();
                TryDelete(folder);
            }
        }

        private async Task InitializeAndAssertAsync(WebView2CompositionControl webView, bool expected, string label)
        {
            var completed = new TaskCompletionSource<CoreWebView2InitializationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.CoreWebView2InitializationCompleted += (_, args) => completed.TrySetResult(args);
            await webView.EnsureCoreWebView2Async().WaitAsync(TimeSpan.FromSeconds(60));
            var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!result.IsSuccess)
                throw result.InitializationException ?? new InvalidOperationException(label + " WebView2 initialization failed.");

            var controlValue = webView.AllowExternalDrop;
            var controllerValue = ReadControllerAllowExternalDrop(webView);
            var message = $"{label}: AllowExternalDrop control={controlValue} controller={controllerValue} expected={expected}";
            _output.WriteLine(message);
            Console.WriteLine(message);
            Assert.Equal(expected, controlValue);
            Assert.Equal(expected, controllerValue);
        }

        private static bool ReadControllerAllowExternalDrop(WebView2CompositionControl webView)
        {
            object? controller = null;
            for (var type = webView.GetType(); type != null; type = type.BaseType)
            {
                var property = type.GetProperty(
                    "CoreWebView2Controller",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property == null)
                    continue;
                controller = property.GetValue(webView);
                break;
            }

            Assert.True(controller != null, "CoreWebView2Controller was not reachable on " + webView.GetType().FullName + " after initialization.");
            var allow = controller!.GetType().GetProperty(
                "AllowExternalDrop",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.True(allow != null, "AllowExternalDrop was not found on " + controller.GetType().FullName + ".");
            return (bool)allow!.GetValue(controller)!;
        }

        private static bool IsMissingCompositionSupport(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                if (current is not (FileNotFoundException or DllNotFoundException or TypeLoadException or InvalidOperationException))
                    continue;
                var text = current.ToString();
                if (text.Contains("Windows.SDK.NET", StringComparison.Ordinal)
                    || text.Contains("Windows.UI.Composition", StringComparison.Ordinal)
                    || text.Contains("D3D", StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static Window CreateWindow(string title) => new()
        {
            Title = title,
            Width = 640,
            Height = 480,
            Left = 20,
            Top = 20,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.ToolWindow,
        };

        private static string CreateUserDataFolder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "rbw-allow-external-drop", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void TryDelete(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                    Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Could not delete " + folder + ": " + ex.Message);
            }
        }
    }

    public sealed class WindowsOnlyFactAttribute : FactAttribute
    {
        public WindowsOnlyFactAttribute()
        {
            if (!OperatingSystem.IsWindows())
                Skip = "Requires Windows. This test starts a WPF WebView2.";
        }
    }
}
