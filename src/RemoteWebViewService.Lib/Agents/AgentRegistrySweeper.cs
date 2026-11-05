using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>Marks agents offline 90 s after their last message and forgets them after 7 days.</summary>
    public sealed class AgentRegistrySweeper(AgentRegistry registry, ILogger<AgentRegistrySweeper> logger) : BackgroundService
    {
        private const string Category = "PeakSWC.RemoteWebView.Agents.AgentRegistrySweeper";
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval);
            try
            {
                while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    try
                    {
                        var result = registry.Sweep();
                        foreach (var agent in result.WentOffline)
                            AgentEvents.Info(logger, Category, $"Agent went offline (no message for {AgentRegistry.OfflineAfter.TotalSeconds:0} s): {agent}");
                        foreach (var agent in result.Forgotten)
                            AgentEvents.Info(logger, Category, $"Agent removed from the list (offline for {AgentRegistry.ForgetAgentAfter.TotalDays:0} days): {agent}");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Agent registry sweep failed");
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
        }
    }
}
