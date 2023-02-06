using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Net;

namespace PeakSWC.RemoteWebView
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) => Host.CreateDefaultBuilder(args).ConfigureWebHostDefaults(webBuilder =>
            {
#if NOAUTHORIZATION
                webBuilder.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 5001, listenOptions => { listenOptions.UseHttps(); }));
#endif
                webBuilder.UseStartup<Startup>();
            });     
    }
}
