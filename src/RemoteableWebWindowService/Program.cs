using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
//using System.Windows.Forms;
using System.Net;
using Microsoft.JSInterop.Infrastructure;
using Microsoft.AspNetCore.Components;
using System.Threading;
using System.Windows.Forms;

namespace PeakSwc.RemoteableWebWindows
{
    public class Program
    {
        public static Form form;
        public static Dispatcher dispatcher;
        [STAThread]
        public static void Main(string[] args)
        {
            //Application.Current
            dispatcher = Dispatcher.CreateDefault();
            form = new Form
            {
                Visible = false,
                WindowState = FormWindowState.Minimized
            };



            //var ww = new WebWindowTunnel(new Uri("https://localhost:443"), new Uri("https://localhost:5001"));
            //ww.Start();


            Task.Run(() => CreateHostBuilder(args).Build().Run());

            //CreateHostBuilder(args).Build().Run();


            Application.Run(form);

        }

        // Additional configuration is required to successfully run gRPC on macOS.
        // For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options => {
                        //options.Listen(IPAddress.Loopback, 80);
                        //options.Listen(IPAddress.Parse( "13.64.108.0"), 443, listenOptions => { listenOptions.UseHttps(); });

                        //options.Listen(IPAddress.Parse("10.1.0.4"), 443, lo => { lo.UseHttps("a419da49-1b24-460a-8397-6be2d80c41f2.pfx", ""); });
                        //options.Listen(IPAddress.Parse("18.217.178.146"), 80, lo => { lo.UseHttps("poc_certificate.pfx", "boldtek@2020"); });
                        options.Listen(IPAddress.Loopback, 443, listenOptions => { listenOptions.UseHttps(); });
                    });
                    webBuilder.UseStartup<Startup>();
                });
    }
}
