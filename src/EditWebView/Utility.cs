using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using LibGit2Sharp;
using System.Text.Json;
using System.Net.Http.Headers;

namespace EditWebView
{
    public static partial class Utility
    {
        private static readonly HttpClient _httpClient = new();
        private static readonly Encoding _windows1252Encoding;

        private const string AspNetCoreRepo = "dotnet/aspnetcore";
        private const string MauiRepo = "dotnet/maui";

        private static readonly Regex ReleaseTagRegex = new(
            @"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<channel>rc|preview)\.(?<n>\d+))?(?:\.(?<build>\d+(?:\.\d+)*))?$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static async Task<string> GetLatestFrameworkVersionAsync(string framework, int majorVersion)
        {
            if (framework != AspNetCoreRepo && framework != MauiRepo)
            {
                throw new ArgumentException($"Unsupported framework: {framework}. Use '{AspNetCoreRepo}' or '{MauiRepo}'.");
            }

            var tags = await ListRemoteTagsAsync(framework);
            string latest = framework == MauiRepo
                ? SelectLatestMauiTag(tags, majorVersion)
                : SelectLatestAspNetTag(tags, majorVersion);
            return $"https://github.com/{framework}/archive/refs/tags/{latest}.zip";
        }

        public static string SelectLatestMauiTag(IEnumerable<string> tags, int majorVersion)
        {
            var parsed = tags
                .Select(ParseReleaseTag)
                .Where(tag => tag != null && tag.Major == majorVersion)
                .Select(tag => tag!)
                .Where(tag => !(majorVersion == 9 && tag.Name.StartsWith("9.0.100-preview.1", StringComparison.Ordinal)))
                .ToList();

            if (parsed.Count == 0)
                throw new Exception($"No valid version {majorVersion} release found for {MauiRepo}.");

            // .NET 11 MAUI moved prereleases to 11.0.100-rc/preview. Older majors use 9.0.0 / 10.0.0.
            // Servicing GA tags use the patch number (10.0.110, 10.0.80, 9.0.120).
            bool hasHundredBand = parsed.Any(tag => tag.Patch >= 100);
            var candidates = hasHundredBand
                ? parsed.Where(tag => tag.Patch >= 100)
                : parsed.Where(tag => tag.Patch < 100);

            return candidates.OrderByDescending(tag => tag, ReleaseTagComparer.Instance).First().Name;
        }

        public static string SelectLatestAspNetTag(IEnumerable<string> tags, int majorVersion)
        {
            var parsed = tags
                .Where(name => name.StartsWith($"v{majorVersion}.", StringComparison.Ordinal))
                .Select(ParseReleaseTag)
                .Where(tag => tag != null && tag.Major == majorVersion)
                .Select(tag => tag!)
                .ToList();

            if (parsed.Count == 0)
                throw new Exception($"No valid version {majorVersion} release found for {AspNetCoreRepo}.");

            return parsed.OrderByDescending(tag => tag, ReleaseTagComparer.Instance).First().Name;
        }

        private static async Task<IReadOnlyList<string>> ListRemoteTagsAsync(string framework)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"ls-remote --tags https://github.com/{framework}.git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git ls-remote.");
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"git ls-remote failed for {framework}: {error}");

            var tags = new List<string>();
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length != 2 || !parts[1].StartsWith("refs/tags/", StringComparison.Ordinal))
                    continue;
                var name = parts[1]["refs/tags/".Length..];
                if (name.EndsWith("^{}", StringComparison.Ordinal))
                    continue;
                tags.Add(name);
            }

            return tags;
        }

        private static ReleaseTag? ParseReleaseTag(string name)
        {
            var match = ReleaseTagRegex.Match(name);
            if (!match.Success)
                return null;

            var channel = match.Groups["channel"].Success ? match.Groups["channel"].Value : "ga";
            int rank = channel switch
            {
                "ga" => 2,
                "rc" => 1,
                _ => 0,
            };
            int prerelease = match.Groups["n"].Success ? int.Parse(match.Groups["n"].Value) : 0;
            var build = match.Groups["build"].Success
                ? match.Groups["build"].Value.Split('.').Select(int.Parse).ToArray()
                : Array.Empty<int>();

            return new ReleaseTag(
                name,
                int.Parse(match.Groups["major"].Value),
                int.Parse(match.Groups["minor"].Value),
                int.Parse(match.Groups["patch"].Value),
                rank,
                prerelease,
                build);
        }

        private sealed record ReleaseTag(string Name, int Major, int Minor, int Patch, int ChannelRank, int Prerelease, int[] Build);

        private sealed class ReleaseTagComparer : IComparer<ReleaseTag>
        {
            public static readonly ReleaseTagComparer Instance = new();

            public int Compare(ReleaseTag? x, ReleaseTag? y)
            {
                if (x == null || y == null)
                    return 0;

                int channel = x.ChannelRank.CompareTo(y.ChannelRank);
                if (channel != 0)
                    return channel;
                int prerelease = x.Prerelease.CompareTo(y.Prerelease);
                if (prerelease != 0)
                    return prerelease;
                int build = CompareParts(x.Build, y.Build);
                if (build != 0)
                    return build;
                int minor = x.Minor.CompareTo(y.Minor);
                if (minor != 0)
                    return minor;
                return x.Patch.CompareTo(y.Patch);
            }

            private static int CompareParts(int[] left, int[] right)
            {
                int length = Math.Max(left.Length, right.Length);
                for (int i = 0; i < length; i++)
                {
                    int l = i < left.Length ? left[i] : 0;
                    int r = i < right.Length ? right[i] : 0;
                    if (l != r)
                        return l.CompareTo(r);
                }
                return 0;
            }
        }
        static Utility()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _windows1252Encoding = Encoding.GetEncoding(1252);
        }

        public static async Task<bool> ArchiveExistsAsync(string url)
        {
            if (_httpClient.DefaultRequestHeaders.UserAgent.Count == 0)
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("FrameworkVersionFinder");

            using var head = new HttpRequestMessage(HttpMethod.Head, url);
            using var headResponse = await _httpClient.SendAsync(head, HttpCompletionOption.ResponseHeadersRead);
            if (headResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;
            if (headResponse.IsSuccessStatusCode)
                return true;

            using var get = new HttpRequestMessage(HttpMethod.Get, url);
            get.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
            using var getResponse = await _httpClient.SendAsync(get, HttpCompletionOption.ResponseHeadersRead);
            return getResponse.IsSuccessStatusCode || getResponse.StatusCode == System.Net.HttpStatusCode.PartialContent;
        }

        public static async Task DownloadZipFileAsync(string url, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or empty.", nameof(url));
            if (string.IsNullOrWhiteSpace(destinationPath))
                throw new ArgumentException("Destination path cannot be null or empty.", nameof(destinationPath));

            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fileStream);

                Console.WriteLine($"File downloaded successfully to {destinationPath}");
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Error downloading file: {e.Message}");
                throw;
            }
        }

        public static List<string> GetOutOfDateFiles(string repoPath)
        {
            if (string.IsNullOrWhiteSpace(repoPath))
                throw new ArgumentException("Repository path cannot be null or empty.", nameof(repoPath));

            try
            {
                using var repo = new Repository(repoPath);
                var status = repo.RetrieveStatus();

                return status.Modified
                    .Concat(status.Staged)
                    .Concat(status.Untracked)
                    .Concat(status.Missing)
                    .Select(entry => entry.FilePath)
                    .Distinct()
                    .ToList();
            }
            catch (RepositoryNotFoundException)
            {
                Console.WriteLine($"Warning: Git repository not found at path: {repoPath}");
                return [];
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while accessing the git repository: {ex.Message}");
                return [];
            }
        }

        public static void UnzipFile(string zipFilePath, string extractPath)
        {
            if (!File.Exists(zipFilePath))
                throw new FileNotFoundException("The specified zip file does not exist.", zipFilePath);

            Directory.CreateDirectory(extractPath);

            using var archive = ZipFile.OpenRead(zipFilePath);
            foreach (var entry in archive.Entries)
            {
                var destinationPath = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));

                if (!destinationPath.StartsWith(Path.GetFullPath(extractPath), StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Attempted to extract file outside of destination directory.");

                if (string.IsNullOrEmpty(entry.Name))
                    Directory.CreateDirectory(destinationPath);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? "");
                    entry.ExtractToFile(destinationPath, true);
                }
            }

            Console.WriteLine($"Successfully extracted zip file to {extractPath}");
        }

        public static string GetFilenameFromUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or empty.", nameof(url));

            var match = UtilityHelpers.MyRegex().Match(url);
            if (!match.Success)
                throw new ArgumentException("Invalid URL format. Unable to extract version.", nameof(url));

            string version = match.Groups[1].Value;

            if (url.Contains("dotnet/aspnetcore"))
                return $"aspnetcore-{version}.zip";
            else if (url.Contains("dotnet/maui"))
                return $"maui-{version}.zip";
            else
                throw new ArgumentException("Unsupported repository URL.", nameof(url));
        }

        public static void ConvertUtf8ToWindows1252(string filePath)
        {
            try
            {
                string content = File.ReadAllText(filePath, Encoding.UTF8);
                File.WriteAllText(filePath, content, _windows1252Encoding);
                //Console.WriteLine($"Successfully converted {filePath} from UTF-8 to Windows-1252 encoding.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting file encoding: {ex.Message}");
            }
        }

        public static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(sourceDir, targetDir));
            }

            foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
            {
                File.Copy(file, file.Replace(sourceDir, targetDir), true);
            }
        }

        public static void ConvertUnixToWindowsLineEndings(string filePath)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                if (!content.Contains("\r\n") && content.Contains('\n'))
                {
                    content = content.Replace("\n", "\r\n");
                    File.WriteAllText(filePath, content, Encoding.UTF8);
                    //Console.WriteLine($"Successfully converted {filePath} to Windows line endings.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting line endings: {ex.Message}");
            }
        }

        public static void DeleteDirectoryAndContents(string path, bool recursive = true)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));

            if (!Directory.Exists(path))
            {
                Console.WriteLine($"Directory not found: {path}");
                return;
            }

            try
            {
                Directory.Delete(path, recursive);
                Console.WriteLine($"Deleted directory: {path}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting directory: {ex.Message}");
            }
        }
    }
}