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
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (serviceDictionary.TryRemove(id, out var client))
            {
                try
                {
                    if (exception != null)
                        logger.LogError(exception, "Shutting down client {Id} due to exception.", id);

                    if (!client.Task.IsCompleted)
                    {
                        client.TrySetCanceled();
                    }
                    else
                    {
                        var serviceState = await client.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                        try
                        {
                            await (serviceState.IPC?.ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "shutdown:" }) ?? Task.CompletedTask).ConfigureAwait(false);
                        }
                        catch (Exception writeEx)
                        {
                            logger.LogError(writeEx, "Failed writing shutdown message to client {Id}.", id);
                        }
                        serviceState.InUse = false;
                        await serviceState.DisposeAsync().ConfigureAwait(false);
                    }
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
