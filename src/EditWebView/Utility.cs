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
        private static readonly Encoding _windows1252Encoding = CreateWindows1252Encoding();

        private const string AspNetCoreRepo = "dotnet/aspnetcore";
        private const string MauiRepo = "dotnet/maui";

        [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:-(?<channel>rc|preview)\.(?<n>\d+))?(?:\.(?<build>\d+(?:\.\d+)*))?$", RegexOptions.CultureInvariant | RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
        private static partial Regex ReleaseTagRegex();

        private static Encoding CreateWindows1252Encoding()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }

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
                throw new InvalidOperationException($"No valid version {majorVersion} release found for {MauiRepo}.");

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
                throw new InvalidOperationException($"No valid version {majorVersion} release found for {AspNetCoreRepo}.");

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
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var match = ReleaseTagRegex().Match(name);
            if (!match.Success)
                return null;

            if (!int.TryParse(match.Groups["major"].Value, out var major) ||
                !int.TryParse(match.Groups["minor"].Value, out var minor) ||
                !int.TryParse(match.Groups["patch"].Value, out var patch))
            {
                return null;
            }

            var channel = match.Groups["channel"].Success ? match.Groups["channel"].Value : "ga";
            int rank = channel switch
            {
                "ga" => 2,
                "rc" => 1,
                _ => 0,
            };
            int prerelease = 0;
            if (match.Groups["n"].Success && !int.TryParse(match.Groups["n"].Value, out prerelease))
                return null;

            var build = Array.Empty<int>();
            if (match.Groups["build"].Success)
            {
                var segments = match.Groups["build"].Value.Split('.');
                build = new int[segments.Length];
                for (int i = 0; i < segments.Length; i++)
                {
                    if (!int.TryParse(segments[i], out build[i]))
                        return null;
                }
            }

            return new ReleaseTag(
                name,
                major,
                minor,
                patch,
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

                var fullExtractRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(extractPath)) + Path.DirectorySeparatorChar;
                if (!destinationPath.StartsWith(fullExtractRoot, StringComparison.OrdinalIgnoreCase) && !string.Equals(destinationPath, Path.TrimEndingDirectorySeparator(Path.GetFullPath(extractPath)), StringComparison.OrdinalIgnoreCase))
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

            var cleanUrl = url.Split(['?', '#'], StringSplitOptions.None)[0];
            var match = UtilityHelpers.MyRegex().Match(cleanUrl);
            if (!match.Success)
                throw new ArgumentException("Invalid URL format. Unable to extract version.", nameof(url));

            string version = match.Groups[1].Value;

            if (url.Contains("dotnet/aspnetcore", StringComparison.OrdinalIgnoreCase))
                return $"aspnetcore-{version}.zip";
            else if (url.Contains("dotnet/maui", StringComparison.OrdinalIgnoreCase))
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting file encoding: {ex.Message}");
            }
        }

        public static void CopyDirectory(string sourceDir, string targetDir)
        {
            if (string.IsNullOrWhiteSpace(sourceDir))
                throw new ArgumentException("Source directory cannot be null or empty.", nameof(sourceDir));
            if (string.IsNullOrWhiteSpace(targetDir))
                throw new ArgumentException("Target directory cannot be null or empty.", nameof(targetDir));

            var dirs = Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories);
            var files = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories);

            Directory.CreateDirectory(targetDir);

            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, dir)));
            }

            foreach (var file in files)
            {
                var destFile = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file));
                var destParent = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrEmpty(destParent))
                    Directory.CreateDirectory(destParent);
                File.Copy(file, destFile, true);
            }
        }

        public static void ConvertUnixToWindowsLineEndings(string filePath)
        {
            try
            {
                string content = File.ReadAllText(filePath);
                string normalized = content.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
                if (!string.Equals(content, normalized, StringComparison.Ordinal))
                {
                    File.WriteAllText(filePath, normalized, Encoding.UTF8);
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
                if (recursive)
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        var attrs = File.GetAttributes(file);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
                    }
                    foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    {
                        var attrs = File.GetAttributes(dir);
                        if ((attrs & FileAttributes.ReadOnly) != 0)
                            File.SetAttributes(dir, attrs & ~FileAttributes.ReadOnly);
                    }
                }
                var rootAttrs = File.GetAttributes(path);
                if ((rootAttrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, rootAttrs & ~FileAttributes.ReadOnly);

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