// BlazorWebViewFormFactory.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.WindowsForms;

namespace WebdriverTestProject
{
    public static class BlazorWebViewFormFactory
    {
        private static Thread? staThread;
        private static AutoResetEvent threadInitialized = new AutoResetEvent(false);
        private static readonly AutoResetEvent threadShutdown = new AutoResetEvent(false);
        public static Form? MainForm { get; set; } = null;

        public static BlazorWebView? CreateBlazorComponent(RootComponent rootComponent)
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
                    control.Dock = DockStyle.Fill;
                    control.Location = new System.Drawing.Point(0, 0);
                    control.Size = new System.Drawing.Size(1440, 1215);
                    control.StartPath = "/";

                    MainForm.AutoScaleDimensions = new System.Drawing.SizeF(10F, 25F);
                    MainForm.AutoScaleMode = AutoScaleMode.Font;
                    MainForm.ClientSize = new System.Drawing.Size(1440, 1215);

                    control.Parent = BlazorWebViewFormFactory.MainForm;
                    MainForm.Controls.Add(control);
                    MainForm.ResumeLayout(false);
                    MainForm.Show();
                }

                //threadInitialized = new AutoResetEvent(false);
                threadInitialized.Set();
            });
            threadInitialized.WaitOne();
            threadInitialized = new AutoResetEvent(false);
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
}
