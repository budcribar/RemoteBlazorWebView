using EditWebView;
using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    private const string AspNetUrl = "https://github.com/dotnet/aspnetcore/archive/refs/tags/v9.0.0-preview.6.24328.4.zip";
    private const string MauiUrl = "https://github.com/dotnet/maui/archive/refs/tags/9.0.0-preview.6.24327.7.zip";
    private const string RelativePath = "../../../../../";

    static async Task Main()
    {
        try
        {
            Console.WriteLine("Starting processing of ASP.NET Core framework...");
            await ProcessAspNetFramework(AspNetUrl);

            Console.WriteLine("Starting processing of MAUI framework...");
            await ProcessMauiFramework(MauiUrl);

            Console.WriteLine("Updating local files...");
            UpdateLocalFiles();

            Console.WriteLine("All operations completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Environment.Exit(1);
        }
    }

    static async Task ProcessAspNetFramework(string url)
    {
        var (destinationPath, _) = await DownloadAndExtractFramework(url);
        CopyWebJSFiles(destinationPath);
    }

    static async Task ProcessMauiFramework(string url)
    {
        var (destinationPath, destinationFolder) = await DownloadAndExtractFramework(url);

        string maui = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(destinationPath));

        ProcessFiles(Path.Combine(maui, @"src\BlazorWebView\src\WindowsForms"), Path.Combine(RelativePath, "RemoteBlazorWebView.WinForms"));
        ProcessFiles(Path.Combine(maui, @"src\BlazorWebView\src\Wpf"), Path.Combine(RelativePath, "RemoteBlazorWebView.Wpf"));
        ProcessFiles(Path.Combine(maui, @"src\BlazorWebView\src\SharedSource"), Path.Combine(RelativePath, "SharedSource"));
    }

    static async Task<(string destinationPath, string destinationFolder)> DownloadAndExtractFramework(string url)
    {
        string zipFile = Utility.GetFilenameFromUrl(url);
        string destinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", zipFile);
        string destinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (!File.Exists(destinationPath))
        {
            Console.WriteLine($"Downloading {zipFile}...");
            await Utility.DownloadZipFileAsync(url, destinationPath);
        }

        string extractedFolder = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(destinationPath));
        if (!Directory.Exists(extractedFolder))
        {
            Console.WriteLine($"Extracting {zipFile}...");
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
        var webJSTarget = Path.Combine(RelativePath, "RemoteWebView.Blazor.JS", "Web.JS");
        Utility.DeleteDirectoryAndContents(webJSTarget);

        var webJSource = Path.Combine(destinationPath.Replace(".zip", ""), "src", "components", "Web.JS");
        Utility.CopyDirectory(webJSource, webJSTarget);

        Console.WriteLine($"Copied Web.JS files from {webJSource} to {webJSTarget}");
    }

    static void ProcessFiles(string inputDir, string outputDir)
    {
        if (!Directory.Exists(inputDir))
        {
            throw new DirectoryNotFoundException($"Input directory not found: {inputDir}");
        }

        Directory.CreateDirectory(outputDir);
        Console.WriteLine($"Processing files from {inputDir} to {outputDir}");

        var files = Directory.EnumerateFiles(inputDir, "*.*", SearchOption.AllDirectories);
        int processedCount = 0;
        int skippedCount = 0;

        foreach (var file in files)
        {
            string relativePath = Path.GetRelativePath(inputDir, file);
            string outputPath = Path.Combine(outputDir, relativePath);
            string outputFileDir = Path.GetDirectoryName(outputPath);

            if (Path.GetExtension(file).Equals(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                skippedCount++;
                continue;
            }

            try
            {
                Directory.CreateDirectory(outputFileDir);

                var editor = new Editor(file);
                editor.ApplyEdits();
                editor.WriteAllText(outputPath);

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
        var repoPath = Path.Combine(Directory.GetCurrentDirectory(), RelativePath);
        var updated = Utility.GetOutOfDateFiles(repoPath);

        foreach (var file in updated)
        {
            string fullPath = Path.Combine(repoPath, file);
            Utility.ConvertUnixToWindowsLineEndings(fullPath);
            Utility.ConvertUtf8ToWindows1252(fullPath);
            Console.WriteLine($"Updated: {file}");
        }
    }
}