using System.Collections.Concurrent;
using System;
using System.Threading;

namespace PeakSWC.RemoteWebView.RemoteStaticFiles
{
    public class FileStats
    {
        public static void Update(ServiceState serviceState, string clientId, FileMetadata metadata)
        {
            if (serviceState == null || metadata == null || metadata.StatusCode != 200 || metadata.Length < 0)
                return;

            Interlocked.Increment(ref serviceState.TotalFilesRead);
            Interlocked.Add(ref serviceState.TotalBytesRead, metadata.Length);
        }

    }
}
