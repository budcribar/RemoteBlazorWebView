using EditWebView;
using System.IO;

class Program
{
    //private const string AspNetUrl = "https://github.com/dotnet/aspnetcore/archive/refs/tags/v9.0.0-preview.7.24406.2.zip";
   // private const string MauiUrl = " https://github.com/dotnet/maui/archive/refs/tags/9.0.0-preview.7.24407.4.zip";

    private const string AspNetCoreRepo = "dotnet/aspnetcore";
    private const string MauiRepo = "dotnet/maui";
    private const int TargetMajorVersion = 11;
    // Optional pins. Env vars ASPNETCORE_TAG and MAUI_TAG override these.
    // When set, the tag archive URL is used directly and the GitHub tag scan is skipped.
    // A 404 falls back to the scan.
    private const string? AspNetCoreTagHint = "v11.0.0-rc.2.26504.105";
    private const string? MauiTagHint = "11.0.100-rc.2.26513.2";
    private const string RelativePath = "../../../../../";
    private static readonly string RepoPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    static async Task Main(string[] args)
    {
        try
        {
            int majorVersion = ResolveMajorVersion(args);
            if (args.Contains("--resolve-only"))
            {
                var majors = ReadMajorArguments(args);
                if (majors.Count == 0)
                    majors.Add(majorVersion);

                foreach (var major in majors)
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string url = await Utility.GetLatestFrameworkVersionAsync(MauiRepo, major);
                    sw.Stop();
                    Console.WriteLine($"MAUI {major}: {url} (resolved in {sw.Elapsed.TotalSeconds:F1}s)");
                }
                return;
            }

            var aspSw = System.Diagnostics.Stopwatch.StartNew();
            string latestAspNetCoreUrl = await ResolveArchiveUrlAsync(AspNetCoreRepo, majorVersion, AspNetCoreTagHint, "ASPNETCORE_TAG");
            aspSw.Stop();
            Console.WriteLine($"Latest ASP.NET Core Version {majorVersion} URL: {latestAspNetCoreUrl} (resolved in {aspSw.Elapsed.TotalSeconds:F1}s)");

            var mauiSw = System.Diagnostics.Stopwatch.StartNew();
            string latestMauiUrl = await ResolveArchiveUrlAsync(MauiRepo, majorVersion, MauiTagHint, "MAUI_TAG");
            mauiSw.Stop();
            Console.WriteLine($"Latest MAUI Version {majorVersion} URL: {latestMauiUrl} (resolved in {mauiSw.Elapsed.TotalSeconds:F1}s)");

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

    static int ResolveMajorVersion(string[] args)
    {
        var fromArgs = ReadMajorArguments(args);
        if (fromArgs.Count == 1)
            return fromArgs[0];

        var env = Environment.GetEnvironmentVariable("TARGET_MAJOR_VERSION");
        if (int.TryParse(env, out int major) && major > 0)
            return major;

        return TargetMajorVersion;
    }

    static List<int> ReadMajorArguments(string[] args)
    {
        var majors = new List<int>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--major" && i + 1 < args.Length && int.TryParse(args[i + 1], out int major))
            {
                majors.Add(major);
                i++;
            }
        }
        return majors;
    }

    static async Task<string> ResolveArchiveUrlAsync(string repo, int majorVersion, string? defaultTag, string envVar)
    {
        // A present env var replaces the const, including an empty value that forces a scan.
        var envTag = Environment.GetEnvironmentVariable(envVar);
        var tag = envTag != null ? envTag : defaultTag;

        if (!string.IsNullOrWhiteSpace(tag))
        {
            string url = $"https://github.com/{repo}/archive/refs/tags/{tag}.zip";
            Console.WriteLine($"Checking pinned tag {tag} for {repo}...");
            if (await Utility.ArchiveExistsAsync(url))
            {
                Console.WriteLine($"Using pinned tag {tag}. Skipping tag scan.");
                return url;
            }

            Console.WriteLine($"Pinned tag {tag} was not found. Falling back to tag scan.");
        }

        return await Utility.GetLatestFrameworkVersionAsync(repo, majorVersion);
    }

    static async Task ProcessAspNetFramework(string url)
    {
        Console.WriteLine("Processing ASP.NET Core framework...");
        var (destinationPath, _) = await DownloadAndExtractFramework(url);
        CopyWebJSFiles(destinationPath);
    }

    static async Task ProcessMauiFramework(string url)
    {
        Console.WriteLine("Processing MAUI framework...");
        var (destinationPath, destinationFolder) = await DownloadAndExtractFramework(url);
        string maui = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(destinationPath));

        ProcessFiles(Path.Combine(maui, "src", "BlazorWebView", "src", "WindowsForms"), "RemoteBlazorWebView.WinForms");
        ProcessFiles(Path.Combine(maui, "src", "BlazorWebView", "src", "Wpf"), "RemoteBlazorWebView.Wpf");
        ProcessFiles(Path.Combine(maui, "src", "BlazorWebView", "src", "SharedSource"), "SharedSource");
    }

    static async Task<(string destinationPath, string destinationFolder)> DownloadAndExtractFramework(string url)
    {
        string zipFile = Utility.GetFilenameFromUrl(url);
        string destinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(destinationFolder);
        string destinationPath = Path.Combine(destinationFolder, zipFile);

        if (!File.Exists(destinationPath))
        {
            Console.WriteLine($"Downloading {zipFile}...");
            var downloadSw = System.Diagnostics.Stopwatch.StartNew();
            await Utility.DownloadZipFileAsync(url, destinationPath);
            downloadSw.Stop();
            Console.WriteLine($"Download of {zipFile} finished in {downloadSw.Elapsed.TotalSeconds:F1}s");
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
        var webJSTarget = Path.Combine(RelativePath, "RemoteWebView.Blazor.JS", "Web.JS");
        var webJSource = Path.Combine(Path.GetDirectoryName(destinationPath) ?? "", Path.GetFileNameWithoutExtension(destinationPath), "src", "Components", "Web.JS");

        Console.WriteLine($"Copying Web.JS files from {webJSource} to {webJSTarget}");

        // Repo-local files that upstream no longer ships. Stash them across the delete/copy.
        var localOverlays = new[]
        {
            Path.Combine("src", "tsconfig.json"),
            Path.Combine("src", "webpack.config.js"),
        };
        var preserved = new Dictionary<string, string>();
        foreach (var relative in localOverlays)
        {
            var full = Path.Combine(webJSTarget, relative);
            if (File.Exists(full))
                preserved[relative] = File.ReadAllText(full);
        }

        Utility.DeleteDirectoryAndContents(webJSTarget);
        Utility.CopyDirectory(webJSource, webJSTarget);

        foreach (var pair in preserved)
        {
            var full = Path.Combine(webJSTarget, pair.Key);
            if (File.Exists(full))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, pair.Value);
            Console.WriteLine($"Restored repo-local Web.JS file {pair.Key}");
        }

        PatchVendoredDotNetRuntime(webJSTarget);
    }

    static void PatchVendoredDotNetRuntime(string webJSTarget)
    {
        var packageJson = Path.Combine(webJSTarget, "package.json");
        if (!File.Exists(packageJson))
            return;

        var text = File.ReadAllText(packageJson);
        const string upstream = "\"@microsoft/dotnet-runtime\": \"*\"";
        const string vendored = "\"@microsoft/dotnet-runtime\": \"file:../dotnet-runtime\"";
        if (!text.Contains(upstream))
            return;

        File.WriteAllText(packageJson, text.Replace(upstream, vendored));
        Console.WriteLine("Pointed @microsoft/dotnet-runtime at the vendored dotnet-runtime package.");
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