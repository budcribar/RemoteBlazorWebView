using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using LibGit2Sharp;

public static class Utility
{
    public static async Task DownloadZipFileAsync(string url, string destinationPath)
    {
        using (var httpClient = new HttpClient())
        {
            try
            {
                // Send a GET request to the specified URL
                using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                {
                    // Ensure we got a successful response
                    response.EnsureSuccessStatusCode();

                    // Open a stream to the destination file
                    using (var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        // Copy the content from the response to the file stream
                        await response.Content.CopyToAsync(fileStream);
                    }
                }
                Console.WriteLine($"File downloaded successfully to {destinationPath}");
            }
            catch (HttpRequestException e)
            {
                Console.WriteLine($"Error downloading file: {e.Message}");
            }
        }
    }
    public static List<string> GetOutOfDateFiles(string repoPath)
    {
        List<string> outOfDateFiles = new List<string>();

        try
        {
            using (var repo = new Repository(repoPath))
            {
                // Get the status of the entire working directory
                RepositoryStatus status = repo.RetrieveStatus();

                // Add modified files
                outOfDateFiles.AddRange(status.Modified.Select(entry => entry.FilePath));

                // Add staged files
                outOfDateFiles.AddRange(status.Staged.Select(entry => entry.FilePath));

                // Add untracked files
                outOfDateFiles.AddRange(status.Untracked.Select(entry => entry.FilePath));

                // Add missing files (deleted but not staged)
                outOfDateFiles.AddRange(status.Missing.Select(entry => entry.FilePath));

                // Remove duplicates (in case a file is both modified and staged)
                outOfDateFiles = outOfDateFiles.Distinct().ToList();
            }
        }
        catch (RepositoryNotFoundException)
        {
            Console.WriteLine($"Error: Git repository not found at path: {repoPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }

        return outOfDateFiles;
    }

    public static void UnzipFile(string zipFilePath, string extractPath)
    {
        try
        {
            // Ensure the zip file exists
            if (!File.Exists(zipFilePath))
            {
                throw new FileNotFoundException("The specified zip file does not exist.", zipFilePath);
            }

            // Ensure the extract path exists, create it if it doesn't
            Directory.CreateDirectory(extractPath);

            // Extract the contents of the zip file
            using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destinationPath = Path.GetFullPath(Path.Combine(extractPath, entry.FullName));

                    // Ensure the destination path is within the extract path (security check)
                    if (!destinationPath.StartsWith(Path.GetFullPath(extractPath), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("Attempted to extract file outside of destination directory.");
                    }

                    // If the entry is a directory, create it
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        // Ensure the directory exists before extracting the file
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                        entry.ExtractToFile(destinationPath, true);
                    }
                }
            }

            Console.WriteLine($"Successfully extracted zip file to {extractPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting zip file: {ex.Message}");
        }
    }
    public static string GetAspNetCoreFilename(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Version cannot be null or empty.", nameof(version));
        }

        // Validate version format (simple check for x.y.z format)
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+$"))
        {
            throw new ArgumentException("Invalid version format. Expected format: x.y.z", nameof(version));
        }

        return $"aspnetcore-{version}.zip";
    }

    public static string GetAspNetCoreFilenameFromUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be null or empty.", nameof(url));
        }

        // Extract version from URL using regex
        var match = Regex.Match(url, @"/v(\d+\.\d+\.\d+)\.zip$");
        if (!match.Success)
        {
            throw new ArgumentException("Invalid URL format. Unable to extract version.", nameof(url));
        }

        string version = match.Groups[1].Value;
        return GetAspNetCoreFilename(version);
    }

    public static string ExtractVersionFromPath(string path)
    {
        // Get the last part of the path (filename or last directory name)
        string lastPart = Path.GetFileName(path);

        // If the last part is empty (in case the path ends with a directory separator),
        // get the directory name
        if (string.IsNullOrEmpty(lastPart))
        {
            lastPart = Path.GetFileName(Path.GetDirectoryName(path)) ?? "";
        }

        return Path.GetFileNameWithoutExtension(lastPart) ?? "";
        // Try to parse the last part as a version
    }

    static Utility()
    {
        // Register the code pages encoding provider
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static void ConvertUtf8ToWindows1252(string filePath)
    {
        try
        {
            // Ensure the Windows-1252 encoding is available
            Encoding windows1252 = Encoding.GetEncoding(1252);

            // Read the entire file content as UTF-8
            string content;
            using (StreamReader reader = new StreamReader(filePath, Encoding.UTF8))
            {
                content = reader.ReadToEnd();
            }

            // Write the content back to the file using Windows-1252 encoding
            using (StreamWriter writer = new StreamWriter(filePath, false, windows1252))
            {
                writer.Write(content);
            }

            Console.WriteLine($"Successfully converted {filePath} from UTF-8 to Windows-1252 encoding.");
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"Error: The file {filePath} was not found.");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"An I/O error occurred: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied: {ex.Message}");
        }
        catch (EncoderFallbackException ex)
        {
            Console.WriteLine($"Encoding error: {ex.Message}");
            Console.WriteLine("Some characters in the file may not be representable in Windows-1252 encoding.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
        }
    }

    public static void CopyDirectory(string sourceDir, string targetDir)
    {
        // Check if source directory exists
        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        // Create target directory if it doesn't exist
        Directory.CreateDirectory(targetDir);

        // Copy all directories
        foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(sourceDir, targetDir));
        }

        // Copy all files
        foreach (var file in Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories))
        {
            var destFile = file.Replace(sourceDir, targetDir);
            File.Copy(file, destFile, true);
        }
    }

    public static void ConvertUnixToWindowsLineEndings(string filePath)
    {
        try
        {
            // Read the entire file content
            string content = File.ReadAllText(filePath);

            // Check if the file needs conversion
            if (!content.Contains("\r\n") && content.Contains("\n"))
            {
                // Replace Unix line endings with Windows line endings
                content = content.Replace("\n", "\r\n");

                // Write the modified content back to the same file
                File.WriteAllText(filePath, content, Encoding.UTF8);

                Console.WriteLine($"Successfully converted {filePath} to Windows line endings.");
            }
            else
            {
                Console.WriteLine($"The file {filePath} already uses Windows line endings or contains no line breaks.");
            }
        }
        catch (FileNotFoundException)
        {
            Console.WriteLine($"Error: The file {filePath} was not found.");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"An I/O error occurred: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
        }
    }

    public static void DeleteDirectoryAndContents(string path, bool recursive = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Path cannot be null or empty.", nameof(path));
        }

        if (!Directory.Exists(path))
        {
            Console.WriteLine($"Directory not found: {path}");
            return;
        }

        try
        {
            // Delete all files in the directory
            foreach (string file in Directory.GetFiles(path))
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                Console.WriteLine($"Deleted file: {file}");
            }

            // Delete all subdirectories
            if (recursive)
            {
                foreach (string dir in Directory.GetDirectories(path))
                {
                    DeleteDirectoryAndContents(dir, recursive);
                }
            }

            // Delete the directory itself
            Directory.Delete(path);
            Console.WriteLine($"Deleted directory: {path}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied: {ex.Message}");
        }
        catch (IOException ex)
        {
            Console.WriteLine($"I/O error: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An unexpected error occurred: {ex.Message}");
        }
    }

}