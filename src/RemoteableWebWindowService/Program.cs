using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.IO;

namespace PeakSwc.RemoteableWebWindows
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
                    webBuilder.ConfigureKestrel(options => {
                        
                        // Webwindow.westus.cloudapp.azure.com Private IP address
                        // Unable to start Kestrel Socket Exception (10013) - need to stop IIS
                        // MUST bind to internal IP address !!
                        
                        if (File.Exists ("cert.pfx"))
                            options.Listen(IPAddress.Parse("10.1.0.4"), 443, lo => { lo.UseHttps("cert.pfx", ""); });
                        else 
                            options.Listen(IPAddress.Loopback, 443, listenOptions => { listenOptions.UseHttps();});
                    });
                    //webBuilder.UseStaticWebAssets();
                    webBuilder.UseStartup<Startup>();
                });
    }
}
