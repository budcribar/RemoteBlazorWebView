using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

namespace PeakSWC.RemoteWebView
{
    public class RemoteFileResolver(ILogger<RemoteFileResolver> logger, ServerFileSyncManager manager)
    {
        public Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subpath)
        {
            return manager.RequestFileMetadataAsync(clientId, subpath, logger);
        }
        public async Task<FileStream> GetFileStreamAsync(string clientId, string subpath)
        {
            if (Path.GetFileName(subpath) == "blazor.modules.json")
            {
                return new FileStream { StatusCode = HttpStatusCode.OK, Stream = new MemoryStream(Encoding.ASCII.GetBytes("[]")) };
            }

            DataRequest dataRequest = await manager.RequestFileDataAsync(clientId, subpath,logger);

            return new FileStream { StatusCode = (HttpStatusCode)dataRequest.StatusCode, Stream = dataRequest.Pipe.Reader.AsStream() };
        }
    }
}
