using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;

namespace WpfStartupProbe;

/// <summary>
/// Separates a missing desktop session, a WebView2 runtime failure, and
/// WebView2CompositionControl startup (new Compositor / D3D) from the tutorial app.
/// Each mode is a separate process so one failure does not hide the next.
/// </summary>
internal static class Program
{
    private static string LogPath = "probe.log";

    [STAThread]
    private static int Main(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "compositor";
        if (args.Length > 1)
            LogPath = args[1];

        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("UNHANDLED " + e.ExceptionObject);

        try
        {
            Log("mode=" + mode
                + " interactive=" + Environment.UserInteractive
                + " session=" + Environment.GetEnvironmentVariable("SESSIONNAME"));
            return mode == "compositor" ? ProbeCompositor() : ProbeControl(mode);
        }
        catch (Exception ex)
        {
            Log("MAIN " + ex);
            return 1;
        }
    }

    private static int ProbeCompositor()
    {
        var compositor = new Windows.UI.Composition.Compositor();
        Log("Compositor ok " + compositor.GetType().FullName);
        compositor.Dispose();
        return 0;
    }

    private static int ProbeControl(string mode)
    {
        var app = new Application();
        app.DispatcherUnhandledException += (_, e) =>
        {
            Log("DispatcherUnhandled " + e.Exception);
            e.Handled = true;
            app.Shutdown(2);
        };
        app.Startup += (_, _) =>
        {
            try
            {
                var composition = mode.StartsWith("composition", StringComparison.Ordinal);
                FrameworkElement control = composition
                    ? new WebView2CompositionControl()
                    : new WebView2();
                if (!mode.EndsWith("nodrop", StringComparison.Ordinal))
                {
                    if (control is WebView2 webView)
                        webView.AllowExternalDrop = false;
                    if (control is WebView2CompositionControl compositionControl)
                        compositionControl.AllowExternalDrop = false;
                    Log("AllowExternalDrop=false on " + control.GetType().Name);
                }

                var window = new Window
                {
                    Title = mode,
                    Width = 480,
                    Height = 320,
                    Content = control,
                };
                window.Show();
                var hwnd = new WindowInteropHelper(window).Handle;
                Log("Show hwnd=0x" + hwnd.ToInt64().ToString("X") + " visible=" + window.IsVisible);

                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Log("alive after 4s");
                    app.Shutdown(0);
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                Log("Startup " + ex);
                app.Shutdown(3);
            }
        };

        var code = app.Run();
        Log("Run exit " + code);
        return code;
    }

    private static void Log(string message)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine;
        Console.Write(line);
        File.AppendAllText(LogPath, line);
    }
}
