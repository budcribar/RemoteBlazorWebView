using EditWebView;
using System;
using System.IO;
using System.Threading.Tasks;

class Program
{
    private const string AspNetUrl = "https://github.com/dotnet/aspnetcore/archive/refs/tags/v9.0.0-preview.6.24328.4.zip";
    private const string MauiUrl = "https://github.com/dotnet/maui/archive/refs/tags/9.0.0-preview.6.24327.7.zip";

    private const string AspNetCoreRepo = "dotnet/aspnetcore";
    private const string MauiRepo = "dotnet/maui";
    private const int TargetMajorVersion = 9;
    private const string RelativePath = "../../../../../";
    private static readonly string RepoPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\..\"));

    static async Task Main()
    {
        try
        {
            string latestAspNetCoreUrl = await Utility.GetLatestFrameworkVersionAsync(AspNetCoreRepo, TargetMajorVersion);
            Console.WriteLine($"Latest ASP.NET Core Version {TargetMajorVersion} URL: {latestAspNetCoreUrl}");

            string latestMauiUrl = await Utility.GetLatestFrameworkVersionAsync(MauiRepo, TargetMajorVersion);
            Console.WriteLine($"Latest MAUI Version {TargetMajorVersion} URL: {latestMauiUrl}");

            await ProcessAspNetFramework(latestAspNetCoreUrl);
            await ProcessMauiFramework(latestMauiUrl);
            UpdateLocalFiles();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static async Task ProcessAspNetFramework(string url)
    {
        Console.WriteLine("Processing ASP.NET Core framework...");
        var (destinationPath, destinationFolder) = await DownloadAndExtractFramework(url);
        CopyWebJSFiles(destinationPath);
    }

    static async Task ProcessMauiFramework(string url)
    {
        Console.WriteLine("Processing MAUI framework...");
        var (destinationPath, destinationFolder) = await DownloadAndExtractFramework(url);
        string maui = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(destinationPath));

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
            Console.WriteLine($"Downloading {zipFile}...");
            await Utility.DownloadZipFileAsync(url, destinationPath);
        }
        else
        {
            Console.WriteLine($"{zipFile} already exists. Skipping download.");
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
        var webJSTarget = Path.Combine(RelativePath, @"RemoteWebView.Blazor.JS/Web.JS");
        var webJSource = Path.Combine(Path.GetDirectoryName(destinationPath) ?? "", Path.GetFileNameWithoutExtension(destinationPath), @"src/components/Web.JS");

        Console.WriteLine($"Copying Web.JS files from {webJSource} to {webJSTarget}");
        Utility.DeleteDirectoryAndContents(webJSTarget);
        Utility.CopyDirectory(webJSource, webJSTarget);
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

                var editor = new Editor(file);
                editor.Edit();
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
        Console.WriteLine("Updating local files...");
        var updated = Utility.GetOutOfDateFiles(RepoPath);

        foreach (var file in updated)
        {
            string fullPath = Path.Combine(RepoPath, file);
            Utility.ConvertUnixToWindowsLineEndings(fullPath);
            Utility.ConvertUtf8ToWindows1252(fullPath);
            Console.WriteLine($"Updated: {file}");
        }

        Console.WriteLine($"Local file update complete. Files updated: {updated.Count}");
    }
}