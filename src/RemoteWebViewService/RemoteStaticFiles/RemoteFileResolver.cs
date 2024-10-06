using Microsoft.Extensions.Logging;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public partial class RemoteFileResolver(ILogger<RemoteFileResolver> logger, ServerFileSyncManager manager)
    {
        public Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subpath)
        {
            return manager.RequestFileMetadataAsync(clientId, subpath, logger);
        }
        public async Task<FileStream> GetFileStreamAsync(string clientId, string subpath)
        {
            if (Path.GetFileName(subpath) == "_framework/blazor.modules.json")
            {
                return new FileStream { Stream = new MemoryStream(Encoding.ASCII.GetBytes("[]")) };
            }

            DataRequest dataRequest = await manager.RequestFileDataAsync(clientId, subpath,logger);

            var htmlHostPath = manager.GetHtmlHostPath(clientId);

            // check to see if we need to edit index.html
            if (Path.GetFileName(subpath) == Path.GetFileName(htmlHostPath))
            {
                using StreamReader sr = new(dataRequest.Pipe.Reader.AsStream());
                var contents = await sr.ReadToEndAsync().ConfigureAwait(false);
                var initialLength = contents.Length;
                contents = HrefRegEx().Replace(contents, $"<base href=\"/{clientId}/\"");
                if (contents.Length == initialLength) logger.LogError("Unable to find base.href in the home page");
                dataRequest.Dispose();

                return new FileStream { Stream = new MemoryStream(Encoding.ASCII.GetBytes(contents)) };
            }

            return new FileStream { Stream = dataRequest.Pipe.Reader.AsStream() };
        }
        [GeneratedRegex("<base.*href.*=.*(\"|').*/.*(\"|')", RegexOptions.Multiline)]
        private static partial Regex HrefRegEx();
    }
}
