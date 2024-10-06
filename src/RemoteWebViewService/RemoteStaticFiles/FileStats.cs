using System.Collections.Concurrent;
using System;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public class FileStats
    {
        public static void Update(ServiceState serviceState, string clientId, FileMetadata metadata)
        {
            Interlocked.Increment(ref serviceState.TotalFilesRead);
            Interlocked.Add(ref serviceState.TotalBytesRead, metadata.Length);
        }

    }
}
