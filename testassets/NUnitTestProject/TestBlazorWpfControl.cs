using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading;
using PeakSWC.RemoteBlazorWebView.Wpf;
using System.Windows;

namespace WebdriverTestProject
{
    public static class BlazorWebViewFactory
    {
        private static Thread? staThread;
        private static AutoResetEvent threadInitialized = new AutoResetEvent(false);
        private static AutoResetEvent threadShutdown = new AutoResetEvent(false);

        public static BlazorWebView? CreateBlazorWebView()
        {
            BlazorWebView? control = null;

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
                Window dummyWindow = new Window
                {
                    Visibility = Visibility.Hidden, // Keep the window hidden
                    Width = 0,
                    Height = 0
                };

                app.Startup += (sender, e) =>
                {
                    control = new BlazorWebView();
                    // You can perform additional control initialization here

                    threadInitialized.Set(); // Signal that the control is initialized
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

            return control;
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
        public void TestMirrorProperty ()
        {
            var webView = BlazorWebViewFactory.CreateBlazorWebView();
            Assert.IsNotNull(webView);

            Application.Current.Dispatcher.Invoke(() => { 
                webView.EnableMirrors = true;
                Assert.IsTrue(webView.EnableMirrors);
            });
        
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            BlazorWebViewFactory.Shutdown();
        }
    }
}