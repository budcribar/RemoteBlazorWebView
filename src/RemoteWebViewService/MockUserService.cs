using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class MockUserService : IUserService
    {
        public Task<List<string>> GetUserGroups(string oid) => Task.FromResult<List<string>>(new List<string> { "hp", "test" });
    }
}
