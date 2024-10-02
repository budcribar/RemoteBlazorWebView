using System.IO;
using System.Net;

namespace PeakSWC.RemoteWebView
{
    public class FileStream
    {
        public HttpStatusCode StatusCode { get; set; }
        public required Stream Stream { get; set; }
    }
}
