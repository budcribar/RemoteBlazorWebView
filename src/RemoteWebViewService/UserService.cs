using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{

    public interface IUserService
    {
        Task<List<string>> GetUserGroups(string oid);
    }

    public class UserService : IUserService
    {
        private readonly Task<ProtectedApiCallHelper> _graphApi;
        public UserService(Task<ProtectedApiCallHelper> graphApi)
        {
           _graphApi = graphApi;
        }

        private List<string> GetMembersForGroup(string groupId, string userId, Dictionary<string, string> groupDict, JObject result)
        {
            List<string> results = new();
            var list = result.Property("value")?.Value;
            if (list != null)
                foreach (var members in list)
                {

                    var id = members["id"]?.ToString() ?? string.Empty;
                    if (id == userId)
                        results.Add(groupDict[groupId]);
                }
            return results;
        }

        private Dictionary<string, string> GetGroups(JObject result)
        {
            Dictionary<string, string> groups = new();
            var list = result.Property("value")?.Value;
            if (list != null)
                foreach (var group in list)
                {
                    var name = group["displayName"]?.ToString() ?? string.Empty;
                    var id = group["id"]?.ToString() ?? string.Empty;

                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                        continue;

                    groups[id] = name;
                }
            return groups;
        }

        public async Task<List<string>> GetUserGroups(string oid)
        {
            List<string> groups = new();
            var groupText = await (await _graphApi).CallWebApiAndProcessResultASync($"https://graph.microsoft.com/v1.0/groups");
            if (groupText == null) { return groups; }
            var groupDict = GetGroups(groupText);

            foreach (var groupId in groupDict.Keys)
            {
                var members = await (await _graphApi).CallWebApiAndProcessResultASync($"https://graph.microsoft.com/v1.0/groups/" + groupId + $"/members");
                if (members != null)
                    groups.AddRange(GetMembersForGroup(groupId, oid, groupDict, members));
            }

            // If a user is not in any groups then they are defaulted to the "test" group
            if (!groups.Any())
                groups.Add("test");

            return groups;
        }
    }
}
