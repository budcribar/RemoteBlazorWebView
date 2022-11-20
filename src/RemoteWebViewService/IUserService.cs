using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public interface IUserService
    {
        Task<List<string>> GetUserGroups(string oid);
    }
}
