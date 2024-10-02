using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Net;

namespace PeakSWC.RemoteWebView
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        // Configure the Host Builder
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    // Clear default logging providers and add Console
                    logging.ClearProviders();
                    logging.AddConsole();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.Limits.Http2.MaxStreamsPerConnection = 2000;

                        options.Listen(IPAddress.Loopback, 5001, listenOptions =>
                        {
                            listenOptions.UseHttps();
                            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                        });
                        //options.Listen(IPAddress.Parse("192.168.1.35"), 5002, listenOptions =>
                        //{
                        //    listenOptions.UseHttps("C:\\Certificates\\DevCertificate_192.168.1.35.pfx", "YourStrongPassword");
                        //    listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                        //});
                    });

                });
    }
}
