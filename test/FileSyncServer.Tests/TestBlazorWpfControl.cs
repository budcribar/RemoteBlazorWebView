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
        private static RootComponent CreateRootComponent()
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
            => RunOnUiAsync(AssertHwndAsync);

        private static async Task RunOnUiAsync(Func<Task> action)
        {
            Assert.NotNull(Application.Current);
            await Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap();
        }

        private async Task AssertHwndAsync()
        {
            var bare = new WebView2();
            await ShowAndAssertAsync(bare, expected: true, "hwnd default");

            var blazor = new BlazorWebView();
            var window = CreateWindow("hwnd BlazorWebView");
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
                await InitializeAndAssertAsync(inner, expected: false, "hwnd BlazorWebView");
            }
            finally
            {
                window.Content = null;
                await blazor.DisposeAsync();
                window.Close();
                TryDelete(folder);
            }
        }

        private async Task ShowAndAssertAsync(WebView2 webView, bool expected, string label)
        {
            var folder = CreateUserDataFolder();
            var window = CreateWindow(label);
            try
            {
                webView.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = folder };
                window.Content = webView;
                window.Show();
                window.UpdateLayout();
                await InitializeAndAssertAsync(webView, expected, label);
            }
            finally
            {
                window.Content = null;
                webView.Dispose();
                window.Close();
                TryDelete(folder);
            }
        }

        private async Task InitializeAndAssertAsync(WebView2 webView, bool expected, string label)
        {
            var completed = new TaskCompletionSource<CoreWebView2InitializationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.CoreWebView2InitializationCompleted += (_, args) => completed.TrySetResult(args);
            await webView.EnsureCoreWebView2Async().WaitAsync(TimeSpan.FromSeconds(60));
            var result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!result.IsSuccess)
                throw result.InitializationException ?? new InvalidOperationException(label + " WebView2 initialization failed.");

            var controlValue = webView.AllowExternalDrop;
            bool controllerValue;
            try
            {
                controllerValue = ReadControllerAllowExternalDrop(webView);
            }
            catch (Exception ex)
            {
                var failed = $"{label}: AllowExternalDrop control={controlValue} controller=not reachable expected={expected}";
                _output.WriteLine(failed);
                Console.WriteLine(failed);
                Console.WriteLine(label + " controller read failed: " + ex.Message);
                throw;
            }

            var message = $"{label}: AllowExternalDrop control={controlValue} controller={controllerValue} expected={expected}";
            _output.WriteLine(message);
            Console.WriteLine(message);
            Assert.Equal(expected, controlValue);
            Assert.Equal(expected, controllerValue);
        }

        // WebView2 does not expose CoreWebView2Controller. It keeps an internal WebView2Base
        // in m_webview2Base, and that object owns the controller created by EnsureCoreWebView2Async.
        private static bool ReadControllerAllowExternalDrop(object webView)
        {
            var webview2Base = RequireMember(webView, "m_webview2Base");
            var controller = RequireMember(webview2Base, "CoreWebView2Controller");
            if (controller is CoreWebView2Controller core)
                return core.AllowExternalDrop;

            throw new InvalidOperationException(
                "CoreWebView2Controller on " + webView.GetType().FullName + " was " + controller.GetType().FullName + ".");
        }

        private static object RequireMember(object instance, string name)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags))
                {
                    if (field.Name == name)
                    {
                        var value = field.GetValue(instance);
                        if (value == null)
                            throw new InvalidOperationException(name + " was null on " + instance.GetType().FullName + " after initialization.");
                        return value;
                    }
                }

                foreach (var property in type.GetProperties(flags))
                {
                    if (property.Name == name && property.GetIndexParameters().Length == 0)
                    {
                        var value = property.GetValue(instance);
                        if (value == null)
                            throw new InvalidOperationException(name + " was null on " + instance.GetType().FullName + " after initialization.");
                        return value;
                    }
                }
            }

            throw new InvalidOperationException(name + " was not found on " + instance.GetType().FullName + ".");
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
