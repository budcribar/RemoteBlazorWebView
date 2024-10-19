using FileWatcherClientService;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.DependencyInjection;
internal class Program
{
    private static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
        .UseWindowsService()
        .ConfigureServices((hostContext, services) =>
        {
            services.AddGrpcClient<FileWatcher.FileWatcherService.FileWatcherServiceClient>(options =>
            {
                options.Address = new Uri("https://localhost:5001");
            });
            services.AddHostedService<Worker>();
        })
        .Build();

        await host.RunAsync();
    }
}