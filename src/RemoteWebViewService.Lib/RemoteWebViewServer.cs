using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Net;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public static class RemoteWebViewServer
    {
        public static void Run(string[] args)
        {
            ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
            Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 5001, listenOptions =>
                    {
                        listenOptions.UseHttps();
                        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                    });
                    string certPath = "C:\\Certificates\\DevCertificate_192.168.1.35.pfx";
                    if (File.Exists(certPath))
                        options.Listen(IPAddress.Parse("192.168.1.35"), 5002, listenOptions =>
                        {
                            listenOptions.UseHttps(certPath, "YourStrongPassword");
                            listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
                        });
                });

                webBuilder.UseStartup<Startup>();
            });
    }
}
