using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace FileWatcherServerService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("FileWatcher Server Service is starting.");

            stoppingToken.Register(() => _logger.LogInformation("FileWatcher Server Service is stopping."));

            while (!stoppingToken.IsCancellationRequested)
            {
                // Perform background tasks if necessary
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }

            _logger.LogInformation("FileWatcher Server Service has stopped.");
        }
    }
}
