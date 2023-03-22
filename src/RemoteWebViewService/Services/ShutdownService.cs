using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Services
{
    public class ShutdownService
    {
        private readonly ILogger<RemoteWebViewService> _logger;
        private readonly ConcurrentDictionary<string, ServiceState> _serviceDictionary;
        private readonly ConcurrentDictionary<string, Channel<string>> _serviceStateChannel;

        public ShutdownService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentDictionary<string, ServiceState> serviceDictionary)
        {
            _logger = logger;
            _serviceDictionary = serviceDictionary;
            _serviceStateChannel = serviceStateChannel;
        }

        public void Shutdown(string id, Exception? exception = null)
        {
            if (exception != null)
                _logger.LogError($"Shutting down {id} Exception:{exception.Message}");
            else
                _logger.LogWarning("Shutting down..." + id);

            if (_serviceDictionary.Remove(id, out var client))
            {
                client.IPC?.ClientResponseStream?.WriteAsync(new WebMessageResponse { Response = "shutdown:" });
                client.InUse = false;
                client.Cancel();

                Task[] tasks = new List<Task?> { client.FileReaderTask, client.PingTask, client.IPC?.BrowserTask, client.IPC?.ClientTask }.Where(x => x != null).Cast<Task>().ToArray();

                try
                {
                    Task.WaitAll(tasks);
                    foreach (var t in tasks)
                        t.Dispose();
                }
                finally
                {
                    client.Dispose();
                }

            }
            _serviceStateChannel.Values.ToList().ForEach(x => x.Writer.TryWrite($"Shutdown:{id}"));
        }
    }
}
