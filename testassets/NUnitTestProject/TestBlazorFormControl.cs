using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using PeakSWC.RemoteBlazorWebView.WindowsForms;
//using System.Windows;
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
using System.Diagnostics;
using PeakSWC.RemoteWebView;
using Grpc.Net.Client;
using System.Threading.Channels;
using System.Windows.Forms;
using System.Windows.Threading;

namespace WebdriverTestProject
{

    public static class BlazorWebViewFormFactory
    {
        private static Thread? staThread;
        private static AutoResetEvent threadInitialized = new AutoResetEvent(false);
        private static readonly AutoResetEvent threadShutdown = new AutoResetEvent(false);
        public static Form? MainForm { get; set; } = null;
        public static BlazorWebView? CreateBlazorComponent (RootComponent rootComponent)
        {
            BlazorWebView? control = null;
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddRemoteWindowsFormsBlazorWebView();
            //serviceCollection.AddRemoteBlazorWebViewDeveloperTools();
            serviceCollection.AddLogging(loggingBuilder =>
            {
                loggingBuilder.SetMinimumLevel(LogLevel.Debug).AddFile("Logs.txt", retainedFileCountLimit: 1);
            });

            
            BlazorWebViewFormFactory.MainForm?.Invoke(() =>
            {

                control = new BlazorWebView();
                control.Services = serviceCollection.BuildServiceProvider();
                control.RootComponents.Add(rootComponent);

                if (MainForm != null)
                {
                    MainForm.Controls.Clear();
                    MainForm.Controls.Add(control);
                    MainForm.Show();
                }
                   
              
                threadInitialized = new AutoResetEvent(false);
                threadInitialized.Set();
            });
            threadInitialized.WaitOne();
            return control;
        }

        public static Form? CreateBlazorWindow()
        {
            Form? dummyWindow = null;

            staThread = new Thread(() =>
            {

                dummyWindow = new Form();
                dummyWindow.Load += DummyWindowLoad;
                dummyWindow.Visible = true;
                dummyWindow.Width = 800;
                dummyWindow.Height = 800;

               

                Application.ThreadException += (sender, e) =>
                {
                    var msg = e.ToString();
                };
              

                Application.ApplicationExit += (sender, e) =>
                {
                    threadShutdown.Set(); // Signal that the application is exiting
                };

                // Run the application with the dummy window
                Application.Run(dummyWindow);
            });

            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();

            // Wait for the control to be initialized
            threadInitialized.WaitOne(3000);

            return dummyWindow;
        }

        private static void DummyWindowLoad(object? sender, EventArgs e)
        {
            threadInitialized.Set();
        }

        public static void Shutdown()
        {
            // Signal the STA thread to shut down by shutting down the application
            MainForm?.Invoke(Application.Exit);
            
            
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
    public class TestBlazorFormControl
    {
      
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestSetMirrorPropertyLate()
        {
            var rootComponent = new RootComponent ("#app", typeof(Home),new Dictionary<string, object?>() );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                //webView.GrpcBaseUri = new System.Uri("https://localhost:5002");

                webView.HostPage = @"wwwroot\index.html";

                webView.EnableMirrors = true;

            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestGrouplLate()
        {
            var rootComponent = new RootComponent
          (
              "#app",
               typeof(Home),
               new Dictionary<string, object?>()

          );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);


            BlazorWebViewFormFactory.MainForm?.Invoke(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.Group = "group";
            });
            }

          
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestMarkupslLate()
        {
            var rootComponent = new RootComponent
          (
              "#app",
               typeof(Home),
               new Dictionary<string, object?>()

          );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.Markup = "markup";

            });
        }


        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestSetPingIntervalLate ()
        {
            var rootComponent = new RootComponent
           (
               "#app",
                typeof(Home),
                new Dictionary<string, object?>()

           );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() => {
              
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.PingIntervalSeconds = 50;

            });
        }

        [TestMethod]
        [ExpectedException(typeof(Exception))]
        public void TestSetIdPropertyLate()
        {
            var rootComponent = new RootComponent
           (
               "#app",
                typeof(Home),
                new Dictionary<string, object?>()

           );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() => {

                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.Id = Guid.NewGuid();
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestGrpcBaseUriPropertyLate()
        {
            var rootComponent = new RootComponent
           (
               "#app",
                typeof(Home),
                new Dictionary<string, object?>()

           );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() => {
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.HostPage = @"wwwroot\index.html";
                webView.GrpcBaseUri = new System.Uri("https://localhost:5002");
              
            });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TestServerUriPropertyLate()
        {
            var rootComponent = new RootComponent
            (
                "#app",
                 typeof(Home),
                 new Dictionary<string, object?>()

            );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() => {
                webView.Id = Guid.NewGuid();             
                webView.HostPage = @"wwwroot\index.html";
                webView.ServerUri = new System.Uri("https://localhost:5001");
            });
        }
        [TestMethod]
        public void TestMirrorPropertyOnTime()
        {
            var rootComponent = new RootComponent
           (
               "#app",
                typeof(Home),
                new Dictionary<string, object?>()

           );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                //webView.GrpcBaseUri = new System.Uri("https://localhost:5002");
                webView.EnableMirrors = true;
                webView.HostPage = @"wwwroot\index.html";         

            });
        }

        [TestMethod]
        public void TestPropertiesOnTime()
        {
            var rootComponent = new RootComponent
           (
               "#app",
                typeof(Home),
                new Dictionary<string, object?>()

           );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);

            BlazorWebViewFormFactory.MainForm?.Invoke(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
                webView.GrpcBaseUri = new System.Uri("https://localhost:5001");
                webView.EnableMirrors = true;
                webView.PingIntervalSeconds = 33;
                webView.Group = "group";
                webView.Markup = "markup";
                webView.HostPage = @"wwwroot\index.html";

            });
        }

        public static Process process;
        public TestContext? TestContext { get; set; }

        [ClassInitialize]
        public static async Task InitializeAsync(TestContext testContext)
        {
             string grpcUrl = @"https://localhost:5001/";
              GrpcChannel? channel;
            string? envVarValue = Environment.GetEnvironmentVariable(variable: "Rust");
            if (envVarValue != null)
                grpcUrl = @"https://localhost:5002/";

            channel = GrpcChannel.ForAddress(grpcUrl);
            process = Utilities.StartServer();

            for (int i = 0; i < 10; i++)
            {
                // Wait for server to spin up
                try
                {
                    var ids = new WebViewIPC.WebViewIPCClient(channel).GetIds(new Empty());
                    Assert.AreEqual(0, ids.Responses.Count, "Server has connections at startup");
                    break;
                }
                catch (Exception) { }
                await Task.Delay(1000);
            }

            BlazorWebViewFormFactory.MainForm = BlazorWebViewFormFactory.CreateBlazorWindow();
            
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
            BlazorWebViewFormFactory.Shutdown();
            process?.Kill();
        }
    }
}