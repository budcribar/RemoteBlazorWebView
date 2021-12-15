
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;

namespace PeakSWC.RemoteWebView.Services
{
   
    public class ShutdownService
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private ConcurrentDictionary<string, ServiceState> ServiceDictionary { get; init; }
        private ConcurrentDictionary<string, Channel<string>> _serviceStateChannel;

        public ShutdownService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentDictionary<string, ServiceState> serviceDictionary)
        {
            _logger = logger;
            ServiceDictionary = serviceDictionary;
            _serviceStateChannel = serviceStateChannel;
        }

        public void Shutdown(string id, Exception? exception = null)
        {
            if (exception != null)
                _logger.LogError($"Shutting down {id} Exception:{exception.Message}");
            else
                _logger.LogWarning("Shutting down..." + id);

            if (ServiceDictionary.ContainsKey(id))
            {
                ServiceDictionary.Remove(id, out var client);
                if (client != null)
                {
                    client.IPC.Shutdown();
                    client.InUse = false;
                    client.CancellationTokenSource.Cancel();
                }
            }
            _serviceStateChannel.Values.ToList().ForEach(x => x.Writer.TryWrite($"Shutdown:{id}"));
        }
    }
}
