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
            if (serviceDictionary.TryRemove(id, out var client))
            {
                try
                {
                    if (exception != null)
                        logger.LogError(exception, "Shutting down client {Id} due to exception.", id);

                    var serviceState = await client.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    await (serviceState.IPC?.ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "shutdown:" }) ?? Task.CompletedTask).ConfigureAwait(false);
                    serviceState.InUse = false;
                    await serviceState.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
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
        }
    }
}
