using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using PeakSWC.RemoteBlazorWebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using PeakSWC.RemoteBlazorWebView;
using Microsoft.Extensions.Logging;
using Google.Protobuf.WellKnownTypes;
using System.Collections.Generic;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Linq.Expressions;
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
            serviceCollection.AddSingleton(new BlazorWebViewDeveloperTools { Enabled = true });
            serviceCollection.AddRemoteWindowsFormsBlazorWebView();
           
            serviceCollection.AddLogging(loggingBuilder =>
            {
                loggingBuilder.SetMinimumLevel(LogLevel.Debug).AddFile("Logs.txt", retainedFileCountLimit: 1);
            });

            
            BlazorWebViewFormFactory.MainForm?.Invoke(() =>
            {

                control = new BlazorWebView
                {
                    Services = serviceCollection.BuildServiceProvider()
                };
                control.RootComponents.Add(rootComponent);
             

                if (MainForm != null)
                {
                    MainForm.Controls.Clear();
                    MainForm.SuspendLayout();
                    control.Dock = System.Windows.Forms.DockStyle.Fill;
                    control.Location = new System.Drawing.Point(0, 0);
                    control.Size = new System.Drawing.Size(1440, 1215);
                    control.StartPath = "/";

                    MainForm.AutoScaleDimensions = new System.Drawing.SizeF(10F, 25F);
                    MainForm.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
                    MainForm.ClientSize = new System.Drawing.Size(1440, 1215);

                    control.Parent = BlazorWebViewFormFactory.MainForm;
                    MainForm.Controls.Add(control);
                    MainForm.ResumeLayout(false);
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
        public void TestGroupLate()
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
        public void TestMarkupLate()
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

        [TestMethod]
        public void TestConnectedEvent()
        {
            var rootComponent = new RootComponent
           (
               "#app",
                typeof(Home),
                new Dictionary<string, object?>()

           );
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);
            AutoResetEvent threadInitialized = new AutoResetEvent(false);
            BlazorWebViewFormFactory.MainForm?.Invoke(() => {

                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");
              
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
            Assert.IsTrue(threadInitialized.WaitOne(10000));

           
        }

        [TestMethod]
        public void TestJsDownload()
        {
            var rootComponent = new RootComponent ("#app", typeof(Home),null);
            var webView = BlazorWebViewFormFactory.CreateBlazorComponent(rootComponent);
            Assert.IsNotNull(webView);
            AutoResetEvent threadInitialized = new AutoResetEvent(false);
            BlazorWebViewFormFactory.MainForm?.Invoke(() =>
            {
                webView.Id = Guid.NewGuid();
                webView.ServerUri = new System.Uri("https://localhost:5001");

                webView.EnableMirrors = true;
                webView.Connected += (sender, e) =>
                {
                    webView.WebView.CoreWebView2.Navigate($"{e.Url}mirror/{e.Id}");
                    var user = e.User.Length > 0 ? $"by user {e.User.Length}" : "";
                    BlazorWebViewFormFactory.MainForm.Text += $" Controlled remotely {user}from ip address {e.IpAddress}";
                    //Task.Delay(60000).Wait();
                    //threadInitialized.Set();
                };
                webView.ReadyToConnect += (sender, e) =>
                {
                    webView.NavigateToString($"<a href='{e.Url}app/{e.Id}' target='_blank'> {e.Url}app/{e.Id}</a>");
                    Utilities.OpenUrlInBrowserWithDevTools($"{e.Url}app/{e.Id}");

                };

                // 800 1k files are marginal
                // 80 10k fails
                // 5, 100k fails
                Stopwatch sw = new Stopwatch();
                sw.Start();
                Console.WriteLine("Generating JS files");
                Utilities.GenJavascript(50,100_000);
                Console.WriteLine($"Done Generating JS files in {sw.Elapsed}");
                webView.HostPage = @"wwwroot\index.html";

            });
            //Assert.IsTrue(threadInitialized.WaitOne(100_000));
            Task.Delay(TimeSpan.FromSeconds(20)).Wait();

        }



        public static Process? process;
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