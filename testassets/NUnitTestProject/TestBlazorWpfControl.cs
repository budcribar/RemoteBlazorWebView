using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using PeakSWC.RemoteBlazorWebView.Wpf;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PeakSWC.RemoteBlazorWebView;
using Microsoft.Extensions.Logging;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq.Expressions;
using System.Windows.Controls;

namespace WebdriverTestProject
{

    public static class BlazorWebViewFactory
    {
        private static Thread? staThread;
        private static AutoResetEvent threadInitialized = new AutoResetEvent(false);
        private static readonly AutoResetEvent threadShutdown = new AutoResetEvent(false);
        public static Window? Window { get; set; } = null;


        public static async Task<BlazorWebView?> CreateBlazorComponent (RootComponent rootComponent)
        {
            BlazorWebView? control = null;
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddRemoteWpfBlazorWebView();
            serviceCollection.AddRemoteBlazorWebViewDeveloperTools();
            serviceCollection.AddLogging(loggingBuilder =>
            {
                loggingBuilder.SetMinimumLevel(LogLevel.Debug).AddFile("Logs.txt", retainedFileCountLimit: 1);
            });

            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {

                control = new BlazorWebView();
                control.Services = serviceCollection.BuildServiceProvider();
                control.RootComponents.Add(rootComponent);

                if (Window != null)
                    Window.Content = control;

                control.ApplyTemplate();
                threadInitialized = new AutoResetEvent(false);
                threadInitialized.Set();
            });
            threadInitialized.WaitOne();
            return control;
        }

        public static Window? CreateBlazorWindow()
        {
            Window? dummyWindow = null;

            staThread = new Thread(() =>
            {
                // Ensure an Application instance is available
                Application? app = null;
                if (Application.Current == null)
                {
                    app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                }
                else
                {
                    app = Application.Current;
                }

                // Create a dummy window to enable dispatcher processing
                dummyWindow = new Window
                {
                    Visibility = Visibility.Visible, // Keep the window hidden
                    Width = 800,
                    Height = 800
                };
                app.DispatcherUnhandledException += (sender, e) =>
                {

                };
                app.Startup += (sender, e) =>
                {
                    try
                    {
                       
                        // You can perform additional control initialization here
                        // dummyWindow.Content = control;

                        threadInitialized.Set(); // Signal that the control is initialized
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.ToString());
                    }

                };

                app.Exit += (sender, e) =>
                {
                    threadShutdown.Set(); // Signal that the application is exiting
                };

                // Run the application with the dummy window
                app.Run(dummyWindow);
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();

            // Wait for the control to be initialized
            threadInitialized.WaitOne();

            return dummyWindow;
        }

        public static void Shutdown()
        {
            // Signal the STA thread to shut down by shutting down the application
            Application.Current?.Dispatcher.Invoke(() => Application.Current.Shutdown());

            // Wait for the thread to complete shutdown
            if (staThread != null)
            {
                threadShutdown.WaitOne(); // Ensure shutdown signal is received
                staThread.Join();
                staThread = null;
            }
           
        }
    }

    [TestClass]
    public class TestBlazorWpfControl
    {
      
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task TestSetMirrorPropertyLate()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()

            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.GrpcBaseUri = new System.Uri("https://localhost:5002");

                webView.HostPage = @"wwwroot\index.html";

                webView.EnableMirrors = true;

            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task TestGrouplLate()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()

            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);


            await Application.Current.Dispatcher.InvokeAsync(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                try
                {
                    webView.Group = "group";
                }
                catch (Exception)
                {


                }

            });
            }

          
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task TestMarkupslLate()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()

            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.Markup = "markup";

            });
        }


        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task TestSetPingIntervalLate ()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()
               
            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() => {
              
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.PingIntervalSeconds = 50;

            });
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public async Task TestSetIdPropertyLate()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()

            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() => {

                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.Id = Guid.NewGuid();
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task TestGrpcBaseUriPropertyLate()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()

            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() => {
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.GrpcBaseUri = new System.Uri("https://localhost:5002");
              
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task TestServerUriPropertyLate()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()

            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() => {
                webView.Id = Guid.NewGuid();             
                webView.HostPage = @"wwwroot\index.html";
                webView.ServerUri = new System.Uri("https://localhost:5001");
            });
        }
        [TestMethod]
        public async Task TestMirrorPropertyOnTime()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()

            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.GrpcBaseUri = new System.Uri("https://localhost:5002");
                webView.EnableMirrors = true;
                webView.HostPage = @"wwwroot\index.html";         

            });
        }

        [TestMethod]
        public async Task TestPropertiesOnTime()
        {
            var rootComponent = new RootComponent()
            {
                Selector = "#app",
                ComponentType = typeof(Home),
                Parameters = new Dictionary<string, object?>()

            };
            var webView = await BlazorWebViewFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            await Application.Current.Dispatcher.InvokeAsync(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.GrpcBaseUri = new System.Uri("https://localhost:5002");
                webView.EnableMirrors = true;
                webView.PingIntervalSeconds = 33;
                webView.Group = "group";
                webView.Markup = "markup";
                webView.HostPage = @"wwwroot\index.html";

            });
        }

        public TestContext? TestContext { get; set; }

        [ClassInitialize]
        public static void Initialize(TestContext testContext)
        { 
            BlazorWebViewFactory.Window = BlazorWebViewFactory.CreateBlazorWindow();
            
            string directoryPath = @"."; // Specify the directory path
            string searchPattern = "Logs-*.txt"; // Pattern to match the file names

            try
            {
                // Get all file paths matching the pattern in the specified directory
                string[] filesToDelete = Directory.GetFiles(directoryPath, searchPattern);

                // Iterate over the file paths and delete each file
                foreach (string filePath in filesToDelete)
                {
                    File.Delete(filePath);
                    Console.WriteLine($"Deleted file: {filePath}");
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., directory not found, lack of permissions)
                Console.WriteLine($"An error occurred: {ex.Message}");
            }
        }


        [ClassCleanup]
        public static void Cleanup()
        {
            BlazorWebViewFactory.Shutdown();
        }
    }
}