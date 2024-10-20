using PeakSWC.RemoteWebView;

using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.DependencyInjection;
using FileWatcherClientService;
internal class Program
{
    private static async Task Main(string[] args)
    {
        const string ServerAddress = "https://192.168.1.35:5002";
        var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService()
        .ConfigureServices((hostContext, services) =>
        {
            services.AddGrpcClient<FileWatcherIPC.FileWatcherIPCClient>(options =>
            {
                options.Address = new Uri(ServerAddress);
            });
            services.AddHostedService<Worker>();
        })
        .Build();

        await host.RunAsync();
    }
}