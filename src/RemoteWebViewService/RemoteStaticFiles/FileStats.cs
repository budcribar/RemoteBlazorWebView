using System.Collections.Concurrent;
using System;

namespace PeakSWC.RemoteWebView
{
    public class FileStats
    {
        public static void Update(ConcurrentDictionary<string, ServiceState> serviceDictionary, string clientId, FileMetadata metadata)
        {


            if (serviceDictionary.TryGetValue(clientId, out var serviceState))
            {
                if (serviceState != null && metadata.Length > 0)
                    lock (serviceState)
                    {
                        serviceState.TotalBytesRead += metadata.Length;
                        serviceState.TotalFilesRead++;
                    }
            }
        }

    }
}
