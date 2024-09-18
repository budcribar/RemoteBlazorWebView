using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Services
{
    public class ShutdownService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentDictionary<string, ServiceState> serviceDictionary)
    {
        public async Task Shutdown(string id, Exception? exception = null)
        {
           
            if (serviceDictionary.Remove(id, out var client))
            {
                if (exception != null)
                    logger.LogError($"Shutting down {id} Exception:{exception.Message}");

                await (client.IPC?.ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "shutdown:" }) ?? Task.CompletedTask);     
                client.InUse = false;
               
                await client.DisposeAsync();

                // Notify other service state channels
                foreach (var channel in serviceStateChannel.Values)
                {
                    if (!channel.Writer.TryWrite($"Shutdown:{id}"))
                    {
                        logger.LogError($"Failed to write shutdown notification to channel for {id}.");
                    }
                }
            }
            
        }
    }
}
