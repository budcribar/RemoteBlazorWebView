using System.Linq;
using System.Reflection;

namespace RemoteableWebWindowSite
{
    public class AppVersionInfo
    {
        private string _buildNumber = "";
        private string _buildId = "";
        private string _gitHash = "";
        private string _gitShortHash = "";

        public string BuildNumber
        {
            get
            {
                if (string.IsNullOrEmpty(_buildNumber))
                {
                    var appAssembly = typeof(AppVersionInfo).Assembly;

                    var temp = appAssembly?.GetCustomAttributes(typeof(AssemblyMetadataAttribute))?.Where(x => (x as AssemblyMetadataAttribute)?.Key == "BuildNumber")?.FirstOrDefault();
                    var infoVerAttr = temp as AssemblyMetadataAttribute ?? null;
                    _buildNumber = infoVerAttr == null ? "buildNumber" : infoVerAttr.Value ?? "";
                }

                return _buildNumber;
            }
        }

        public string BuildId
        {
            get
            {
                if (string.IsNullOrEmpty(_buildId))
                {
                    var appAssembly = typeof(AppVersionInfo).Assembly;
                    var temp = appAssembly?.GetCustomAttributes(typeof(AssemblyMetadataAttribute))?.Where(x => (x as AssemblyMetadataAttribute)?.Key == "BuildId")?.FirstOrDefault();
                    var infoVerAttr = temp as AssemblyMetadataAttribute ?? null;

                    _buildId = infoVerAttr == null ? "buildId" : infoVerAttr.Value ?? "buildId";
                }

                return _buildId;
            }
        }

        public string GitHash
        {
            get
            {
                if (string.IsNullOrEmpty(_gitHash))
                {
                    var version = "1.0.0+LOCALBUILD"; // Dummy version for local dev
                    var appAssembly = typeof(AppVersionInfo).Assembly;
                    var temp = appAssembly?.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute)).FirstOrDefault();
                    var infoVerAttr = temp as AssemblyInformationalVersionAttribute ?? null;

                    if (infoVerAttr != null && infoVerAttr.InformationalVersion.Length > 6)
                    {
                        // Hash is embedded in the version after a '+' symbol, e.g. 1.0.0+a34a913742f8845d3da5309b7b17242222d41a21
                        version = infoVerAttr.InformationalVersion;
                    }
                    _gitHash = version[(version.IndexOf('+') + 1)..];

                }

                return _gitHash;
            }
        }

        public string ShortGitHash
        {
            get
            {
                if (string.IsNullOrEmpty(_gitShortHash))
                {
                    _gitShortHash = GitHash.Substring(GitHash.Length - 6, 6);
                }
                return _gitShortHash;
            }
        }
    }
}