using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Services
{
    public class ShutdownService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary)
    {
        public async Task Shutdown(string id, Exception? exception = null)
        {
            PeakSWC.RemoteWebView.RaceLog.Write($"shutdown-service-enter id={id} ex={exception?.GetType().Name ?? "none"}");
            if (serviceDictionary.TryRemove(id, out var client))
            {
                PeakSWC.RemoteWebView.RaceLog.Write($"shutdown-service-removed id={id}");
                try
                {
                    if (exception != null)
                        logger.LogError(exception, "Shutting down client {Id} due to exception.", id);

                    var serviceState = await client.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    PeakSWC.RemoteWebView.RaceLog.Write($"shutdown-service-write id={id} pid={serviceState.Pid}");
                    await (serviceState.IPC?.ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "shutdown:" }) ?? Task.CompletedTask).ConfigureAwait(false);
                    PeakSWC.RemoteWebView.RaceLog.Write($"shutdown-service-write-return id={id}");
                    serviceState.InUse = false;
                    await serviceState.DisposeAsync().ConfigureAwait(false);
                    PeakSWC.RemoteWebView.RaceLog.Write($"shutdown-service-disposed id={id}");
                }
                catch (Exception ex)
                {
                    PeakSWC.RemoteWebView.RaceLog.Write($"shutdown-service-throw id={id} ex={ex.GetType().Name} msg={ex.Message}");
                    logger.LogError(ex, "Failed shutdown for client {Id}.", id);
                }

                try
                {
                    foreach (var channel in serviceStateChannel.Values)
                    {
                        if (!channel.Writer.TryWrite($"Shutdown:{id}"))
                        {
                            logger.LogError("Failed to write shutdown notification to channel for client {Id}.", id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed shutdown for client {Id} during channel notification.", id);
                }
            }
            else
            {
                PeakSWC.RemoteWebView.RaceLog.Write($"shutdown-service-missing id={id}");
            }
        }
    }
}
