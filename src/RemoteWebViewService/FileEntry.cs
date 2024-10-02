using System;
using System.IO.Pipelines;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public class FileEntry : IDisposable
    {
        public string Path { get; set; } = string.Empty;
        public long Length { get; set; } = -1;
        public Pipe Pipe { get; set; } = new Pipe();
        public int Instance {  get; set; } = 0;
        public DateTimeOffset LastModified { get; set; } = DateTimeOffset.MinValue;

        public SemaphoreSlim Semaphore = new SemaphoreSlim(0, 1);

        public SemaphoreSlim FileDataSemaphore = new SemaphoreSlim(0, 1);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Semaphore.Dispose();
                FileDataSemaphore.Dispose();
            }
        }

        ~FileEntry()
        {
            Dispose(false);
        }
    }
   
}
