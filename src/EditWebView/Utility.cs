using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace EditWebView
{
    public static class Utility
    {
        private static readonly HttpClient _httpClient = new();
        private static readonly Encoding _windows1252Encoding;

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

            var match = Regex.Match(url, @"/(?:v)?([\d.-]+(?:-[\w.]+)?)\.zip$");
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