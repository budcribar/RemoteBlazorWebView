using EditWebView;
using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    private const string AspNetUrl = "https://github.com/dotnet/aspnetcore/archive/refs/tags/v9.0.0-preview.6.24328.4.zip";
    private const string MauiUrl = "https://github.com/dotnet/maui/archive/refs/tags/9.0.0-preview.6.24327.7.zip";
    private const string RelativePath = "../../../../../";
    private static readonly string RepoPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\..\"));


    static async Task Main()
    {
        await ProcessAspNetFramework(AspNetUrl);
        await ProcessMauiFramework(MauiUrl);

        UpdateLocalFiles();
    }

    static async Task ProcessAspNetFramework(string url)
    {
        var (destinationPath, destinationFolder) = await DownloadAndExtractFramework(url);
        CopyWebJSFiles(destinationPath);
    }

    static async Task ProcessMauiFramework(string url)
    {
        string zipFile = Utility.GetFilenameFromUrl(url);
        string destinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", zipFile);
        string destinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (!File.Exists(destinationPath))
        {
            await Utility.DownloadZipFileAsync(url, destinationPath);
        }

        string extractedFolder = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(destinationPath));
        if (!Directory.Exists(extractedFolder))
        {
            Utility.UnzipFile(destinationPath, destinationFolder);
        }

        string maui = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(zipFile));

        ProcessFiles(Path.Combine(maui, @"src\BlazorWebView\src\WindowsForms"), "RemoteBlazorWebView.WinForms");
        ProcessFiles(Path.Combine(maui, @"src\BlazorWebView\src\Wpf"), "RemoteBlazorWebView.Wpf");
        ProcessFiles(Path.Combine(maui, @"src\BlazorWebView\src\SharedSource"), "SharedSource");
    }

    static async Task<(string destinationPath, string destinationFolder)> DownloadAndExtractFramework(string url)
    {
        string zipFile = Utility.GetFilenameFromUrl(url);
        string destinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", zipFile);
        string destinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (!File.Exists(destinationPath))
        {
            await Utility.DownloadZipFileAsync(url, destinationPath);
        }

        string extractedFolder = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(destinationPath));
        if (!Directory.Exists(extractedFolder))
        {
            Utility.UnzipFile(destinationPath, destinationFolder);
        }
        else
        {
            Console.WriteLine($"Extracted folder already exists: {extractedFolder}");
        }

        return (destinationPath, destinationFolder);
    }

    static void CopyWebJSFiles(string destinationPath)
    {
        var webJSTarget = Path.Combine(RelativePath, @"RemoteWebView.Blazor.JS/Web.JS");
        Utility.DeleteDirectoryAndContents(webJSTarget);

        var webJSource = Path.Combine(destinationPath.Replace(".zip", ""), @"src/components/Web.JS");
        Utility.CopyDirectory(webJSource, webJSTarget);

        Console.WriteLine($"Copied Web.JS files from {webJSource} to {webJSTarget}");
    }

    static void ProcessFiles(string inputDir, string outputDir)
    {
        if (!Directory.Exists(inputDir))
        {
            throw new DirectoryNotFoundException($"Input directory not found: {inputDir}");
        }

        outputDir = Path.GetFullPath(Path.Combine(RepoPath, "src", outputDir));

        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
            Console.WriteLine($"Created output directory: {outputDir}");
        }

        var files = Directory.EnumerateFiles(inputDir, "*.*", SearchOption.AllDirectories);
        int processedCount = 0;
        int skippedCount = 0;

        foreach (var file in files)
        {
            string relativePath = Path.GetRelativePath(inputDir, file);
            string outputPath = Path.Combine(outputDir, relativePath);
            string outputFileDir = Path.GetDirectoryName(outputPath) ?? "";

            if (Path.GetExtension(file).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                skippedCount++;
                continue;
            }

            try
            {
                if (!Directory.Exists(outputFileDir))
                {
                    Directory.CreateDirectory(outputFileDir);
                }

                // Create an Editor instance with the file path
                var editor = new Editor(file);

                // Apply edits
                editor.ApplyEdits();

                // Write the edited content to the output file
                editor.WriteAllText(outputFileDir);

                processedCount++;
                Console.WriteLine($"Processed: {relativePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing file {relativePath}: {ex.Message}");
            }
        }

        Console.WriteLine($"Processing complete. Files processed: {processedCount}, Files skipped: {skippedCount}");
    }

    static void UpdateLocalFiles()
    {
        var updated = Utility.GetOutOfDateFiles(RepoPath);

        foreach (var file in updated)
        {
            string fullPath = Path.Combine(RepoPath, file);
            Utility.ConvertUnixToWindowsLineEndings(fullPath);
            Utility.ConvertUtf8ToWindows1252(fullPath);
        }
    }
}