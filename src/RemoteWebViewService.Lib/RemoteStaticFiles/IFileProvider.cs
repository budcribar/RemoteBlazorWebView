using Microsoft.Extensions.FileProviders;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.RemoteStaticFiles
{
    public interface IFileProvider
    {
        Task<IFileInfo?> GetFileInfo(string subpath);

        Task<bool> FileStreamExistsAsync(string subpath);
    }
}
