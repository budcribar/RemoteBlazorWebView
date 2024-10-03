using System;
using System.IO.Pipelines;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public class FileEntry
    {
        public string Path { get; set; } = string.Empty;
        public long Length { get; set; } = -1;
        public Pipe Pipe { get; set; } = new Pipe();
        public int Instance {  get; set; } = 0;
        public DateTimeOffset LastModified { get; set; } = DateTimeOffset.MinValue;
    }
   
}
