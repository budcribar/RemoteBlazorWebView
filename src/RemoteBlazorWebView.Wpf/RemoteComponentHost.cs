using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PeakSWC.RemoteBlazorWebView.RemoteComponents;

namespace PeakSWC.RemoteBlazorWebView.Wpf
{
    /// <summary>What <see cref="RemoteComponentHost.Run"/> needs.</summary>
    public sealed class RemoteComponentHostOptions
    {
        /// <summary>The relay the app frames (e.g. an embedded RemoteWebViewServer's BaseUri).</summary>
        public required Uri ServerUri { get; init; }

        /// <summary>The relay's gRPC address, when it isn't <see cref="ServerUri"/> (an embedded relay on plain HTTP has two ports).</summary>
        public Uri? GrpcBaseUri { get; init; }

        /// <summary>The client id the app frames: &lt;ServerUri&gt;/&lt;Id&gt;.</summary>
        public required Guid Id { get; init; }

        /// <summary>The root component the frame shows.</summary>
        public required Type RootComponent { get; init; }

        public string Selector { get; init; } = "#app";

        /// <summary>The host page, relative to the exe. It should include <see cref="RemoteComponentFrame.HeightScript"/>.</summary>
        public string HostPage { get; init; } = @"wwwroot\index.html";

        /// <summary>Exit when this process (the app) goes away, even if it never got to close the relay. Polls the process list, which
        /// works even when this process runs with fewer rights than the app and can't open its process. When the app holds this process's
        /// input, <see cref="ExitWhenInputCloses"/> does the same without polling.</summary>
        public int? ParentProcessId { get; init; }

        /// <summary>
        /// Exit when <see cref="Input"/> (by default stdin) ends. Windows closes a pipe's write end when the process holding it exits, also
        /// when it crashes or is killed, so an app that starts this process with a pipe on its stdin and keeps the write end open for the
        /// component's lifetime gets the same as <see cref="ParentProcessId"/>, at once and without polling. Off by default: a process
        /// started without redirected input would see its input end right away. The input is read even when no receiver is registered.
        /// </summary>
        public bool ExitWhenInputCloses { get; init; }

        /// <summary>WebView2's user data folder: required when the default (next to the exe) isn't writable by this process.</summary>
        public string? WebViewDataFolder { get; init; }

        /// <summary>
        /// Let read-only mirrors of the app's page show this component too: they frame the relay's mirror view of it
        /// (RemoteComponentSession.MirrorSource) and can't click or type in it. Default true: the component is only reachable from the app's
        /// page, so its mirrors are the app's mirrors. False makes the frame on a mirror show the relay's "Mirroring is not enabled".
        /// </summary>
        public bool EnableMirrors { get; init; } = true;

        /// <summary>The (never shown) window's title.</summary>
        public string Title { get; init; } = "Remote component";

        /// <summary>Add the root component's services, e.g. <c>services.AddRemoteComponentReceiver&lt;MyReceiver&gt;(connection)</c>.</summary>
        public Action<IServiceCollection, RemoteComponentConnection>? ConfigureServices { get; init; }

        public Action<string>? Log { get; init; }

        /// <summary>The app's messages; default this process's stdin.</summary>
        public TextReader? Input { get; init; }

        /// <summary>Callbacks to the app; default this process's stdout.</summary>
        public TextWriter? Output { get; init; }
    }

    /// <summary>
    /// Runs a remote component: a RemoteBlazorWebView client of the app's relay whose page the app shows in a sandboxed iframe
    /// (<see cref="RemoteComponentFrameBase"/>). The window only holds the control and is never seen. Exits when the frame is
    /// reloaded or closed (the app starts a fresh copy each time), or when the app goes away.
    /// </summary>
    public static class RemoteComponentHost
    {
        public const int ExitOk = 0, ExitFailed = 1;

        /// <summary>Runs until the frame or the app goes away; returns the exit code. Call it from an [STAThread] Main.</summary>
        public static int Run(RemoteComponentHostOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            var log = options.Log ?? (_ => { });
            try
            {
                if (options.WebViewDataFolder is { } data)
                {
                    Directory.CreateDirectory(data);
                    Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", data);
                }

                var connection = new RemoteComponentConnection(options.Input ?? Console.In, options.Output ?? Console.Out);
                connection.Closed += () => log("the app closed the input");
                connection.HandlerFailed += ex => log("a message failed: " + ex.Message);

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (_, e) => { log("crash: " + e.Exception); e.Handled = true; app.Shutdown(ExitFailed); };
                if (options.ParentProcessId is { } parent) ExitWithParent(app, parent, log);
                if (options.ExitWhenInputCloses)
                    connection.Closed += () => { log("the input ended: the app exited or closed it"); app.Dispatcher.BeginInvoke(() => app.Shutdown(ExitOk)); };

                var services = new ServiceCollection();
                services.AddRemoteWpfBlazorWebView();
                services.AddSingleton(connection);
                options.ConfigureServices?.Invoke(services, connection);
                var provider = services.BuildServiceProvider();
                if (options.ExitWhenInputCloses)
                {
                    // Something must read the input to see it end: the receivers (started now rather than on first use), or else a reader
                    // that ignores what it reads.
                    var receivers = provider.GetServices<RemoteComponentReceiverBase>().ToList();
                    if (receivers.Count == 0) _ = Task.Run(() => connection.RunAsync(_ => Task.CompletedTask));
                }

                var webView = new BlazorWebView
                {
                    Services = provider,
                    ServerUri = options.ServerUri,
                    GrpcBaseUri = options.GrpcBaseUri ?? options.ServerUri,
                    Id = options.Id,
                    EnableMirrors = options.EnableMirrors,
                };
                webView.RootComponents.Add(new RootComponent { Selector = options.Selector, ComponentType = options.RootComponent });
                webView.Connected += (_, e) => log($"frame connected from {e.IpAddress}");
                webView.Disconnected += (_, e) => { log($"disconnected: {e.Exception?.Message}"); app.Dispatcher.BeginInvoke(() => app.Shutdown(ExitOk)); };
                webView.Refreshed += (_, _) => { log("frame reloaded"); app.Dispatcher.BeginInvoke(() => app.Shutdown(ExitOk)); };
                // Setting HostPage starts the control, so it goes last.
                webView.HostPage = options.HostPage;

                // People see the app's frame, never this window. It must not be minimized: the WebView2 composition control creates a
                // D3D9 device from it when it loads, which fails for a minimized window. So: normal, off screen, out of the taskbar,
                // never activated.
                var window = new Window
                {
                    Title = options.Title, Width = 800, Height = 600, Content = webView,
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false,
                };
                window.Show();
                return app.Run();
            }
            catch (Exception ex) { log("failed: " + ex); return ExitFailed; }
        }

        private static void ExitWithParent(Application app, int parentPid, Action<string> log)
        {
            _ = Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
                while (await timer.WaitForNextTickAsync())
                {
                    bool running;
                    try { running = IsRunning(parentPid); }
                    catch (Exception ex) { log($"can't watch the app (pid {parentPid}): {ex.Message}"); return; }
                    if (running) continue;
                    log("the app exited");
                    await app.Dispatcher.BeginInvoke(() => app.Shutdown(ExitOk));
                    return;
                }
            });
        }

        private static bool IsRunning(int pid)
        {
            var found = false;
            foreach (var p in Process.GetProcesses())
                using (p)
                    if (p.Id == pid) found = true;
            return found;
        }
    }
}
