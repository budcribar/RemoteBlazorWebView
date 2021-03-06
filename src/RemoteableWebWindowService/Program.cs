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
                    webBuilder.ConfigureKestrel(options =>
                    {

                        // Webwindow.westus.cloudapp.azure.com Private IP address
                        //  Unable to start Kestrel Socket Exception(10013) - need to stop IIS
                        // MUST bind to internal IP address !!

                        if (File.Exists("cert.pfx"))
                            options.Listen(IPAddress.Parse("10.1.0.4"), 443, lo => { lo.UseHttps("cert.pfx", string.Empty); });
                        else
                            options.Listen(IPAddress.Loopback, 443, listenOptions => { listenOptions.UseHttps(); });
                    });

                    // Comment out for App Service
                    webBuilder.UseKestrel();
                    
                    // Uncomment for App Service
                    // webBuilder.UseIISIntegration();
                   
                    webBuilder.UseStartup<Startup>();
                });
    }
}
