using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WebdriverTestProject
{
    public class Utilities
    {
        public static Process StartServer()
        {
           
            Stopwatch sw = new Stopwatch();
            sw.Start();

            Process.GetProcesses().FirstOrDefault(p => p.ProcessName == "RemotableWebViewService")?.Kill();
            var relative = @"..\..\..\..\..\src\RemoteableWebWindowService";
            var executable = @"bin\debug\net6\RemoteableWebViewService.exe";
            var f = Path.Combine(Directory.GetCurrentDirectory(), relative, executable);

            Process process = new Process();
            process.StartInfo.FileName = Path.GetFullPath(f);
            process.StartInfo.UseShellExecute = true;

            process.Start();
            Console.WriteLine($"Started server in {sw.Elapsed}");
            return process;
        }

        public static string BlazorWinFormsPath()
        {
            var relative = @"..\..\..\..\..\src\BlazorWinFormsApp";
            var exePath = @"bin\debug\net6-windows";
            return Path.Combine(Directory.GetCurrentDirectory(), relative, exePath);
        }

        public static string BlazorWinFormsAppExe()
        {
            return Path.Combine(BlazorWinFormsPath(), "BlazorWinFormsApp.exe");
        }

        public static Process StartBlazorWinFormsApp()
        {
            var f = BlazorWinFormsAppExe();

            Process.GetProcesses().Where(p => p.ProcessName == "BlazorWinFormsApp").ToList().ForEach(x => x.Kill());

            Stopwatch sw = new();
           
            Process p = new Process();
            p.StartInfo.FileName = Path.GetFullPath(f);
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WorkingDirectory = BlazorWinFormsPath();
            p.Start();
               
            Console.WriteLine($"Clients started in {sw.Elapsed}");

            return p;
        }
      
    }
}
