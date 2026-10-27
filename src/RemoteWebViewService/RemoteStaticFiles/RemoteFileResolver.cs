using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.RemoteStaticFiles
{
    public partial class RemoteFileResolver(ILogger<RemoteFileResolver> logger, ServerFileSyncManager manager)
    {
        public virtual Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subpath, string? requestUri = null, string? contentType = null)
        {
            return manager.RequestFileMetadataAsync(clientId, subpath, logger, requestUri, contentType);
        }
        public virtual async Task<FileStream> GetFileStreamAsync(string clientId, string subpath)
        {
            DataRequest dataRequest = await manager.RequestFileDataAsync(clientId, subpath,logger).ConfigureAwait(false);

            var htmlHostPath = manager.GetHtmlHostPath(clientId);

            // check to see if we need to edit index.html
            if (Path.GetFileName(subpath) == Path.GetFileName(htmlHostPath))
            {
                using StreamReader sr = new(dataRequest.Pipe.Reader.AsStream());
                var contents = await sr.ReadToEndAsync().ConfigureAwait(false);
                var initialLength = contents.Length;
                contents = HrefRegEx().Replace(contents, $"<base$1$2/{clientId}/$2");
                if (contents.Length == initialLength) logger.LogError("Unable to find base.href in the home page");
                dataRequest.Dispose();

                return new FileStream { Stream = new MemoryStream(Encoding.UTF8.GetBytes(contents)) };
            }

            return new FileStream { Stream = dataRequest.Pipe.Reader.AsStream() };
        }
        // Stay inside the base tag. A greedy match ate later quoted attributes on the same line.
        [GeneratedRegex("<base([^>]*href\\s*=\\s*)([\"'])[^\"']*\\2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex HrefRegEx();
    }
}
