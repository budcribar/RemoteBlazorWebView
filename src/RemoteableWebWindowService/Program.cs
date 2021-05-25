using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Net;

namespace PeakSwc.RemoteableWebWindows
{
    public class Program
    {     
        [STAThread]
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }
     
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options => {
                        //options.Listen(IPAddress.Loopback, 80);
                        //options.Listen(IPAddress.Parse( "13.64.108.0"), 443, listenOptions => { listenOptions.UseHttps(); });

                        // Webwindow.westus.cloudapp.azure.com
                        //options.Listen(IPAddress.Parse("10.1.0.4"), 443, lo => { lo.UseHttps("a419da49-1b24-460a-8397-6be2d80c41f2.pfx", ""); });

                        //options.Listen(IPAddress.Parse("18.217.178.146"), 80, lo => { lo.UseHttps("poc_certificate.pfx", "boldtek@2020"); });

                        // localhost
                        options.Listen(IPAddress.Loopback, 443, listenOptions => { listenOptions.UseHttps(); });
                        //options.Listen(IPAddress.Loopback, 443, listenOptions => { listenOptions.UseHttps(); listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1AndHttp2; });
                    });
                    webBuilder.UseStartup<Startup>();
                });
    }
}
