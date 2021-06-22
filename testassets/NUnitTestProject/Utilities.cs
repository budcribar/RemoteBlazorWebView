using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WebdriverTestProject
{
    public class Utilities
    {
        public static Process StartServer()
        {

            Stopwatch sw = new();
            sw.Start();

            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "RemoteableWebViewService")?.Kill();
            var relative = @"..\..\..\..\..\src\RemoteableWebWindowService";
            var executable = @"bin\debug\net6\RemoteableWebViewService.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), relative, executable);

            Process process = new();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.UseShellExecute = true;

            process.Start();
            Console.WriteLine($"Started server in {sw.Elapsed}");
            return process;
        }

        public static string BlazorWinFormsDebugPath()
        {
            var relative = @"..\..\..\..\..\src\BlazorWinFormsApp";
            var exePath = @"bin\debug\net6.0-windows10.0.19041";  
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }
        public static string BlazorWinFormsDebugAppExe()
        {
            return Path.Combine(BlazorWinFormsDebugPath(), "BlazorWinFormsApp.exe");
        }

        public static string BlazorWinFormsPath()
        {
            var relative = @"..\..\..\..\..\src\BlazorWinFormsApp";
            //var exePath = @"bin\debug\net6-windows";
            var exePath = "publish";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }
        public static string BlazorWinFormsAppExe()
        {
            return Path.Combine(BlazorWinFormsPath(), "BlazorWinFormsApp.exe");
        }

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

        public static void KillBlazorWinFormsApp()
        {
            Process.GetProcesses().Where(p => p.ProcessName == "BlazorWinFormsApp").ToList().ForEach(x => x.Kill());
        }

        public static Process StartRemoteBlazorWinFormsDebugApp()
        {
            var f = BlazorWinFormsDebugAppExe();

            Stopwatch sw = new();

            Process p = new();
            p.StartInfo.FileName = Path.GetFullPath(f);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = @"-u=https://localhost:443";
            p.StartInfo.WorkingDirectory = BlazorWinFormsPath();
            p.Start();

            Console.WriteLine($"Clients started in {sw.Elapsed}");

            return p;
        }


        public static Process StartRemoteBlazorWinFormsApp()
        {
            var f = BlazorWinFormsAppExe();

            Stopwatch sw = new();

            Process p = new();
            p.StartInfo.FileName = Path.GetFullPath(f);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = @"-u=https://localhost:443";
            p.StartInfo.WorkingDirectory = BlazorWinFormsPath();
            p.Start();

            Console.WriteLine($"Clients started in {sw.Elapsed}");

            return p;
        }

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

        public static Process StartRemoteBlazorWebViewApp()
        {
            var f = BlazorWebViewAppExe();

            Stopwatch sw = new();

            Process p = new();
            p.StartInfo.FileName = Path.GetFullPath(f);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = @"-u=https://localhost:443";
            p.StartInfo.WorkingDirectory = BlazorWebViewPath();
            p.Start();

            Console.WriteLine($"Clients started in {sw.Elapsed}");

            return p;
        }

        public static void KillRemoteBlazorWebViewApp()
        {
            Process.GetProcesses().Where(p => p.ProcessName == "HelloRemotePhotino.Blazor").ToList().ForEach(x => x.Kill());
        }

        public static void KillRemoteBlazorWpfApp()
        {
            Process.GetProcesses().Where(p => p.ProcessName == "RemoteBlazorWebViewTutorial.WpfApp").ToList().ForEach(x => x.Kill());
        }


        public static Process StartRemoteBlazorWpfApp()
        {
            var f = BlazorWpfAppExe();

            Stopwatch sw = new();

            Process p = new();
            p.StartInfo.FileName = Path.GetFullPath(f);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.Arguments = @"-u=https://localhost:443";
            p.StartInfo.WorkingDirectory = BlazorWpfPath();
            p.Start();

            Console.WriteLine($"Clients started in {sw.Elapsed}");

            return p;
        }

    }
}
