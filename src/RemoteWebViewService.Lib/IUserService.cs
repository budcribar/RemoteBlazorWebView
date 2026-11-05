using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public interface IUserService
    {
        Task<IReadOnlyList<string>> GetUserGroups(string oid);
    }
}
