using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class MockUserService : IUserService
    {
        public Task<IReadOnlyList<string>> GetUserGroups(string oid) => Task.FromResult<IReadOnlyList<string>>(["hp", "test"]);
    }
}
