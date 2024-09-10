using Microsoft.Extensions.FileProviders;
using System.Threading.Tasks;

namespace PeakSwc.StaticFiles
{
    public interface IFileProvider
    {
        Task<IFileInfo> GetFileInfo(string subpath);
    }
}
