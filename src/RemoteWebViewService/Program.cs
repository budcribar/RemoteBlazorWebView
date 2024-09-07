using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Net;

namespace PeakSWC.RemoteWebView
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Directory.SetCurrentDirectory(System.AppDomain.CurrentDomain.BaseDirectory);
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder =>
            {
                // Note: If appsettings.json does not exist in Azure the following configuration will fail with a certificate error
                if (!File.Exists("appsettings.json"))
                   webBuilder.ConfigureKestrel(options => { options.Limits.Http2.MaxStreamsPerConnection = 1600;
                       options.Listen(IPAddress.Loopback, 5001, listenOptions => { listenOptions.UseHttps(); listenOptions.Protocols = HttpProtocols.Http1AndHttp2; }); });
                
                webBuilder.UseStartup<Startup>();
            });     
    }
}
