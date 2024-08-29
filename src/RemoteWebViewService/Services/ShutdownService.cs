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
        public void Shutdown(string id, Exception? exception = null)
        {
           
            if (serviceDictionary.Remove(id, out var client))
            {
                if (exception != null)
                    logger.LogError($"Shutting down {id} Exception:{exception.Message}");
                //else
                //    logger.LogWarning("Shutting down..." + id);

                client.IPC?.ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "shutdown:" });     
                client.InUse = false;
                client.Cancel();

                client.Dispose();

                serviceStateChannel.Values.ToList().ForEach(x => x.Writer.TryWrite($"Shutdown:{id}"));

            }
            
        }
    }
}
