// BlazorWebViewFactory.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.Wpf;

namespace WebdriverTestProject
{
    public static class BlazorWebViewFactory
    {
        private static Thread? staThread;
        private static AutoResetEvent threadInitialized = new AutoResetEvent(false);
        private static readonly AutoResetEvent threadShutdown = new AutoResetEvent(false);
        public static Window? Window { get; set; } = null;
        private static Grid? gridContainer = null;

        public static async Task<BlazorWebView?> CreateBlazorComponent(RootComponent rootComponent)
        {
            BlazorWebView? control = null;
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddRemoteWpfBlazorWebView();
            serviceCollection.AddLogging(loggingBuilder =>
            {
                loggingBuilder.SetMinimumLevel(LogLevel.Debug).AddFile("Logs.txt", retainedFileCountLimit: 1);
            });

            threadInitialized = new AutoResetEvent(false);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                control = new BlazorWebView
                {
                    Services = serviceCollection.BuildServiceProvider()
                };
                control.RootComponents.Add(rootComponent);

                if (gridContainer != null)
                {
                    // Add the new control to the grid
                    gridContainer.RowDefinitions.Add(new RowDefinition());

                    int rowIndex = gridContainer.RowDefinitions.Count - 1;
                    Grid.SetRow(control, rowIndex);

                    gridContainer.Children.Add(control);
                }

                control.ApplyTemplate();

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
                    Visibility = Visibility.Hidden, // Keep the window hidden
                    Width = 800,
                    Height = 800,
                    Content = new Grid()
                };

                app.DispatcherUnhandledException += (sender, e) =>
                {
                    // Handle exceptions if necessary
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
}
