using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WebdriverTestProject
{
    public class Utilities
    {
        #region Server
        public static Process StartServer()
        {

            Stopwatch sw = new();
            sw.Start();

            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "RemoteableWebViewService")?.Kill();
            var relative = @"..\..\..\..\..\src\RemoteableWebViewService";
            var executable = @"publish\RemoteableWebViewService.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), relative, executable);

            Process process = new();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.UseShellExecute = true;

            process.Start();
            Console.WriteLine($"Started server in {sw.Elapsed}");
            return process;
        }
        #endregion

        #region WinForm
        public static Process StartRemoteBlazorWinFormsDebugApp() => StartProcess(BlazorWinFormsDebugAppExe(), BlazorWinFormsPath());

        public static Process StartRemoteBlazorWinFormsApp() => StartProcess(BlazorWinFormsAppExe(), BlazorWinFormsPath());

        public static Process StartRemoteEmbeddedBlazorWinFormsApp() => StartProcess(BlazorWinFormsEmbeddedAppExe(), BlazorWinFormsEmbeddedPath());

        public static string BlazorWinFormsDebugPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp";
            var exePath = @"bin\debug\net6.0-windows";  
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }
        public static string BlazorWinFormsDebugAppExe() => Path.Combine(BlazorWinFormsDebugPath(), "RemoteBlazorWebViewTutorial.WinFormsApp.exe");

        public static string BlazorWinFormsPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp";
            var exePath = "publish";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWinFormsEmbeddedPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp";
            var exePath = "publishEmbedded";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWinFormsAppExe() => Path.Combine(BlazorWinFormsPath(), "RemoteBlazorWebViewTutorial.WinFormsApp.exe");

        public static string BlazorWinFormsEmbeddedAppExe() => Path.Combine(BlazorWinFormsEmbeddedPath(), "RemoteBlazorWebViewTutorial.WinFormsApp.exe");

        public static void KillBlazorWinFormsApp()
        {
            Process.GetProcesses().Where(p => p.ProcessName == "RemoteBlazorWebViewTutorial.WinFormsApp").ToList().ForEach(x => x.Kill());
        }
        #endregion

        #region Wpf
        public static string BlazorWpfAppExe()
        {
            return Path.Combine(BlazorWpfPath(), "RemoteBlazorWebViewTutorial.WpfApp.exe");
        }
        public static string BlazorWpfPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp";
            //var exePath = @"bin\debug\net6-windows";
            var exePath = "publish";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWpfEmbeddedPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp";
            //var exePath = @"bin\debug\net6-windows";
            var exePath = "publishEmbedded";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWpfAppEmbeddedExe()
        {
            return Path.Combine(BlazorWpfEmbeddedPath(), "RemoteBlazorWebViewTutorial.WpfApp.exe");
        }

        public static Process  StartRemoteBlazorWpfEmbeddedApp() => StartProcess(BlazorWpfAppEmbeddedExe(), BlazorWpfEmbeddedPath());

        public static Process StartRemoteBlazorWpfApp() => StartProcess(BlazorWpfAppExe(), BlazorWpfPath());

        public static void KillRemoteBlazorWpfApp() => Kill("RemoteBlazorWebViewTutorial.WpfApp");
        #endregion

        #region WebView
        public static string BlazorWebViewPath()
        {
            var relative = @"..\..\..\..\..\src\HelloRemotePhotino.Blazor";
            var exePath = @"bin\debug\net6";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWebViewAppExe()
        {
            return Path.Combine(BlazorWebViewPath(), "HelloRemotePhotino.Blazor.exe");
        }

        public static Process StartRemoteBlazorWebViewApp() => StartProcess(BlazorWebViewAppExe(), BlazorWebViewPath());

        public static void KillRemoteBlazorWebViewApp() => Kill("HelloRemotePhotino.Blazor");

        #endregion

        #region Common
        public static Process StartProcess(string executable, string directory)
        {
            Stopwatch sw = new();

            Process p = new();
            p.StartInfo.FileName = Path.GetFullPath(executable);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = @"-u=https://localhost:443";
            p.StartInfo.WorkingDirectory = directory;
            p.Start();

            Console.WriteLine($"Clients started in {sw.Elapsed}");

            return p;
        }
        public static void Kill(string name) => Process.GetProcesses().Where(p => p.ProcessName == name).ToList().ForEach(x => x.Kill());
        #endregion
    }
}
