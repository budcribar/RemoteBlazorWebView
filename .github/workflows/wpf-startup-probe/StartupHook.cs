using System;
using System.IO;

/// <summary>
/// Loaded via DOTNET_STARTUP_HOOKS before the tutorial app's Main.
/// The WPF app's own handler only shows a MessageBox, which does not leave
/// the exception text in the test log. This runs first and writes it to a file.
/// </summary>
public static class StartupHook
{
    public static void Initialize()
    {
        var path = Environment.GetEnvironmentVariable("WPF_CRASH_LOG");
        if (string.IsNullOrEmpty(path))
            path = Path.Combine(Path.GetTempPath(), "wpf-crash.txt");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            try
            {
                var text = args.ExceptionObject?.ToString() ?? "(null)";
                File.WriteAllText(path, text);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(path + ".hookerror", ex.ToString()); }
                catch { }
            }
        };
    }
}
