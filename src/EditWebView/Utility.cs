using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
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

        public static async Task<string> GetLatestFrameworkVersionAsync(string framework, int majorVersion)
        {
            if (framework != AspNetCoreRepo && framework != MauiRepo)
            {
                throw new ArgumentException($"Unsupported framework: {framework}. Use '{AspNetCoreRepo}' or '{MauiRepo}'.");
            }

            string apiUrl = $"https://api.github.com/repos/{framework}/tags?per_page=100";
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "FrameworkVersionFinder");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "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");

            List<(string Name, DateTime Timestamp)> allTags = new List<(string, DateTime)>();
            string nextUrl = apiUrl;

            while (!string.IsNullOrEmpty(nextUrl))
            {
                var response = await _httpClient.GetAsync(nextUrl);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var tags = JsonSerializer.Deserialize<JsonElement[]>(content);

                foreach (var tag in tags)
                {
                    string name = tag.GetProperty("name").GetString();
                    string commitSha = tag.GetProperty("commit").GetProperty("sha").GetString();
                    DateTime timestamp = await GetCommitTimestamp(framework, commitSha);
                    allTags.Add((name, timestamp));
                }

                nextUrl = GetNextPageUrl(response);
            }

            var validTags = allTags
                .Where(t => IsValidVersionTag(t.Name, majorVersion, framework))
                .Where(t => !(framework == MauiRepo && t.Name.StartsWith("9.0.100-preview.1"))) // Skip 9.0.100-preview.1 for MAUI
                .Select(t => (t.Name, t.Timestamp, VersionInfo: ParseVersion(t.Name, framework)))
                .Where(t => t.VersionInfo.majorVersion == majorVersion)
                .OrderByDescending(t => t.VersionInfo.version.Major)
                .ThenByDescending(t => t.Timestamp)
                .ThenByDescending(t => t.VersionInfo.version.Minor)
                .ThenByDescending(t => t.VersionInfo.version.Build)
                .ThenByDescending(t => t.VersionInfo.previewVersion)
                .ThenByDescending(t => t.VersionInfo.version.Revision)
                .ToList();

            if (!validTags.Any())
            {
                throw new Exception($"No valid version {majorVersion} release found for {framework}.");
            }

            var latestVersion = validTags.First().Name;
            return $"https://github.com/{framework}/archive/refs/tags/{latestVersion}.zip";
        }

        private static async Task<DateTime> GetCommitTimestamp(string framework, string commitSha)
        {
            string commitUrl = $"https://api.github.com/repos/{framework}/commits/{commitSha}";
            var response = await _httpClient.GetAsync(commitUrl);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var commitData = JsonSerializer.Deserialize<JsonElement>(content);
            string dateString = commitData.GetProperty("commit").GetProperty("committer").GetProperty("date").GetString();
            return DateTime.Parse(dateString);
        }

        private static string GetNextPageUrl(HttpResponseMessage response)
        {
            var linkHeader = response.Headers.Contains("Link") ? response.Headers.GetValues("Link").FirstOrDefault() : null;
            if (string.IsNullOrEmpty(linkHeader)) return null;

            var links = linkHeader.Split(',');
            foreach (var link in links)
            {
                var segments = link.Split(';');
                if (segments.Length == 2 && segments[1].Trim().Equals("rel=\"next\"", StringComparison.OrdinalIgnoreCase))
                {
                    return segments[0].Trim(' ', '<', '>');
                }
            }
            return null;
        }

        private static bool IsValidVersionTag(string name, int majorVersion, string framework)
        {
            if (framework == AspNetCoreRepo)
            {
                return name.StartsWith($"v{majorVersion}.");
            }
            else // MauiRepo
            {
                return name.StartsWith($"{majorVersion}.");
            }
        }

        private static (System.Version version, int majorVersion, int previewVersion) ParseVersion(string name, string framework)
        {
            string pattern;
            if (framework == AspNetCoreRepo)
            {
                pattern = @"v(\d+)\.(\d+)\.(\d+)(?:-preview\.(\d+))?(?:\.(\d+))?";
            }
            else // MauiRepo
            {
                pattern = @"(\d+)\.(\d+)\.(\d+)(?:-preview\.(\d+))?(?:\.(\d+))?";
            }

            var match = Regex.Match(name, pattern);
            if (match.Success)
            {
                int major = int.Parse(match.Groups[1].Value);
                int minor = int.Parse(match.Groups[2].Value);
                int patch = int.Parse(match.Groups[3].Value);
                int previewVersion = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 0;
                int buildNumber = match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 0;

                return (new System.Version(major, minor, patch, buildNumber), major, previewVersion);
            }

            return (new System.Version(0, 0, 0), 0, 0);
        }
        static Utility()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _windows1252Encoding = Encoding.GetEncoding(1252);
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