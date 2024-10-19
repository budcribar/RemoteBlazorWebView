using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace FileWatcherServerService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        // Configure the host builder to use Startup and configure as a Windows Service
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseWindowsService() // Enables the application to run as a Windows Service
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>(); // Specifies the Startup class
                    webBuilder.UseKestrel(options =>
                    {
                        options.ListenAnyIP(5001, listenOptions =>
                        {
                            listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2;
                            // For production, configure proper HTTPS certificates
                            listenOptions.UseHttps();
                        });
                    });
                });
    }
}
