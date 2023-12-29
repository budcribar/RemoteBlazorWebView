using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace WebdriverTestProject
{
    public class Utilities
    {
        #region Server
        public static Process StartServer()
        {

            Stopwatch sw = new();
            sw.Start();

            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "RemoteWebViewService")?.Kill();
            var relative = @"..\..\..\..\..\src\RemoteWebViewService\bin\publishNoAuth";
            var executable = @"RemoteWebViewService.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), relative, executable);

            Process process = new();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), relative);
            process.StartInfo.UseShellExecute = true;

            process.Start();
            Console.WriteLine($"Started server in {sw.Elapsed}");
            return process;
        }

        public static Process StartServerFromPackage()
        {

            Stopwatch sw = new();
            sw.Start();

            Process process = new();
            process.StartInfo.FileName = "RemoteWebViewService";
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
            var exePath = @"bin\x64\debug\net8.0-windows";  
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }
        public static string BlazorWinFormsDebugAppExe() => Path.Combine(BlazorWinFormsDebugPath(), "RemoteBlazorWebViewTutorial.WinFormsApp.exe");

        public static string BlazorWinFormsPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin";
            var exePath = "publish";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWinFormsEmbeddedPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin";
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

        public static Process StartRemoteBlazorWpfDebugApp() => StartProcess(BlazorWpfDebugAppExe(), BlazorWpfPath());
        public static string BlazorWpfDebugAppExe() => Path.Combine(BlazorWpfDebugPath(), "RemoteBlazorWebViewTutorial.WpfApp.exe");

        public static string BlazorWpfDebugPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp";
            var exePath = @"bin\x64\debug\net8.0-windows";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWpfAppExe()
        {
            return Path.Combine(BlazorWpfPath(), "RemoteBlazorWebViewTutorial.WpfApp.exe");
        }
        public static string BlazorWpfPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin";
            var exePath = "publish";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWpfEmbeddedPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin";
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

        public static int CountRemoteBlazorWinFormsApp() => Count("RemoteBlazorWebViewTutorial.WinFormsApp");
        public static int CountRemoteBlazorWpfApp() => Count("RemoteBlazorWebViewTutorial.WpfApp");
        #endregion

        #region WebView
        public static string BlazorWebViewDebugPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial";
            var exePath = @"bin\debug\net8.0";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWebViewPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin";
            var exePath = @"publish";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWebViewDebugAppExe()
        {
            return Path.Combine(BlazorWebViewDebugPath(), "RemoteBlazorWebViewTutorial.exe");
        }

        public static string BlazorWebViewAppExe()
        {
            return Path.Combine(BlazorWebViewPath(), "RemoteBlazorWebViewTutorial.exe");
        }

        public static string BlazorWebViewEmbeddedPath()
        {
            var relative = @"..\..\..\..\..\..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin";
            var exePath = "publishEmbedded";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }
      

        public static string BlazorWebViewAppEmbeddedExe()
        {
            return Path.Combine(BlazorWebViewEmbeddedPath(), "RemoteBlazorWebViewTutorial.exe");
        }

        public static Process StartRemoteBlazorWebViewApp() => StartProcess(BlazorWebViewAppExe(), BlazorWebViewPath());
        public static Process StartRemoteBlazorWebViewEmbeddedApp() => StartProcess(BlazorWebViewAppEmbeddedExe(), BlazorWebViewEmbeddedPath());
        public static void KillRemoteBlazorWebViewApp() => Kill("RemoteBlazorWebViewTutorial");

        #endregion

        #region Common

        public static string JavascriptFile = @"..\..\..\..\..\src\RemoteWebView.Blazor.JS\dist\remote.blazor.desktop.js";
        public static Process StartProcess(string executable, string directory)
        {
            Stopwatch sw = new();

            Process p = new();
            p.StartInfo.FileName = Path.GetFullPath(executable);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = @"-u=https://localhost:5001";
            p.StartInfo.WorkingDirectory = directory;
            p.Start();


            // Try to prevent COMException 0x8007139F
            while (p.MainWindowHandle == IntPtr.Zero)
            {
                // Refresh process property values
                p.Refresh();

                // Wait a bit before checking again
                Thread.Sleep(100);
            }

            Console.WriteLine($"Clients started in {sw.Elapsed}");

            return p;
        }

        public static int Count(string name) => Process.GetProcesses().Where(p => p.ProcessName == name).Count();
        public static void Kill(string name) => Process.GetProcesses().Where(p => p.ProcessName == name).ToList().ForEach(x =>
        {
            x.Kill();
            // Wait for exit before re-starting
            x.WaitForExit();
        });
            
        #endregion
    }
}
