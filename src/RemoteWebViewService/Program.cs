using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Net;

namespace PeakSWC.RemoteableWebView
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    if (!File.Exists("appsettings.json"))
                        webBuilder.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 5001, listenOptions => { listenOptions.UseHttps(); }));

                    // Comment out for App Service
                    webBuilder.UseKestrel();
                    
                    // Uncomment for App Service
                    // webBuilder.UseIISIntegration();
                   
                    webBuilder.UseStartup<Startup>();
                });
    }
}
