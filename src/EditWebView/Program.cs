using System.IO;

namespace EditWebView
{
static class Program
{
    private const string AspNetCoreRepo = "dotnet/aspnetcore";
    private const string MauiRepo = "dotnet/maui";
    private const int TargetMajorVersion = 10;
    private const string RelativePath = "../../../../../";
    private static readonly string RepoPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\..\"));

    static async Task Main()
    {
        try
        {
            // What already differs from the last commit, so the summary can say when not everything in it is from this run.
            var changedBefore = ChangeReport.ChangedBefore(RepoPath);

            string latestAspNetCoreUrl = await Utility.GetLatestFrameworkVersionAsync(AspNetCoreRepo, TargetMajorVersion);
            Console.WriteLine($"Latest ASP.NET Core Version {TargetMajorVersion} URL: {latestAspNetCoreUrl}");

            string latestMauiUrl = await Utility.GetLatestFrameworkVersionAsync(MauiRepo, TargetMajorVersion);
            Console.WriteLine($"Latest MAUI Version {TargetMajorVersion} URL: {latestMauiUrl}");

            await ProcessAspNetFramework(latestAspNetCoreUrl);
            await ProcessMauiFramework(latestMauiUrl);
            UpdateLocalFiles();

            ChangeReport.Print(RepoPath, ChangeReport.TagFromUrl(latestAspNetCoreUrl), ChangeReport.TagFromUrl(latestMauiUrl), changedBefore);
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
        var (destinationPath, _) = await DownloadAndExtractFramework(url);
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
        var ours = ReadOurWebJSFiles(webJSTarget);
        ClearWebJS(webJSTarget);
        Utility.CopyDirectory(webJSource, webJSTarget);
        RestoreOurWebJSFiles(webJSTarget, ours);
        string filePath = Path.Combine(webJSTarget, "yarn.lock");
        using (File.Create(filePath))
        {
            Console.WriteLine($"Empty yarn.lock file created at: {filePath}");
        }

        ApplyWebJSPatches(webJSTarget);
    }

    /// <summary>
    /// Files in Web.JS that belong to this repository, not to aspnetcore: the bundle's own tsconfig.json extends the first.
    /// They are read before Web.JS is replaced and written back afterwards, unless upstream brings a file of the same name.
    /// </summary>
    static readonly string[] OurWebJSFiles = ["src/tsconfig.json", "src/webpack.config.js"];

    static Dictionary<string, byte[]> ReadOurWebJSFiles(string webJS)
    {
        var contents = new Dictionary<string, byte[]>();
        foreach (string file in OurWebJSFiles)
        {
            string path = Path.Combine(webJS, file);
            if (File.Exists(path))
            {
                contents[file] = File.ReadAllBytes(path);
                continue;
            }

            // Not in the working folder (an earlier run removed it, say): the committed copy will do.
            try
            {
                contents[file] = ChangeReport.GitBytes(RepoPath, ["show", $"HEAD:src/RemoteWebView.Blazor.JS/Web.JS/{file}"]);
            }
            catch (Exception)
            {
                // Reported by RestoreOurWebJSFiles, once it is known whether upstream supplies the file.
            }
        }

        return contents;
    }

    static void RestoreOurWebJSFiles(string webJS, Dictionary<string, byte[]> contents)
    {
        foreach (string file in OurWebJSFiles)
        {
            string path = Path.Combine(webJS, file);
            if (File.Exists(path))
            {
                WarnPatch($"{file} now comes from upstream; this repository's own copy was not put back.");
                ChangeReport.Patches.Add((file, "NOT KEPT, upstream now has a file of this name: compare it with ours", true));
            }
            else if (contents.TryGetValue(file, out var content))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, content);
                Console.WriteLine($"  kept: {file}");
                ChangeReport.Patches.Add((file, "kept (ours, not in upstream)", false));
            }
            else
            {
                WarnPatch($"{file} is this repository's own file and there was no copy to keep.");
                ChangeReport.Patches.Add((file, "MISSING, no copy in the folder or in the last commit", true));
            }
        }
    }

    /// <summary>
    /// Empties Web.JS before the upstream copy, so a file upstream removed does not stay behind. node_modules is left:
    /// it is not source, 'npm install' brings it up to date, and its link to ../dotnet-runtime is what a delete trips over.
    /// </summary>
    static void ClearWebJS(string webJS)
    {
        if (!Directory.Exists(webJS)) return;

        foreach (string entry in Directory.GetFileSystemEntries(webJS))
        {
            if (Path.GetFileName(entry).Equals("node_modules", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                if (Directory.Exists(entry))
                {
                    foreach (string file in Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
                    }

                    Directory.Delete(entry, true);
                }
                else
                {
                    File.SetAttributes(entry, File.GetAttributes(entry) & ~FileAttributes.ReadOnly);
                    File.Delete(entry);
                }
            }
            catch (Exception ex)
            {
                string name = Path.GetFileName(entry);
                WarnPatch($"Could not remove Web.JS/{name}: {ex.Message}");
                ChangeReport.Patches.Add(($"Web.JS/{name}", "NOT REMOVED before the copy, so files upstream deleted may remain: " + ex.Message, true));
            }
        }
    }

    /// <summary>
    /// Web.JS is replaced wholesale from aspnetcore, so the few changes this repository needs in it are made again here, the way
    /// Editor does for the C# files. Each one checks for the upstream text it replaces and says so loudly when that text is gone:
    /// upstream changed, and the patch needs a look before the result is committed.
    /// </summary>
    static void ApplyWebJSPatches(string webJS)
    {
        Console.WriteLine("Applying RemoteBlazorWebView patches to Web.JS...");

        // A page in a sandboxed iframe (a remote component's frame) has an opaque origin, so this request is cross-origin; a
        // credentialed one can't be answered with the relay's "Access-Control-Allow-Origin: *".
        PatchWebJSFile(Path.Combine(webJS, "src", "JSInitializers", "JSInitializers.WebView.ts"),
            "    credentials: 'include',",
            "    // RemoteBlazorWebView: 'same-origin' instead of upstream's 'include'. A page in a sandboxed iframe has an opaque origin, so\n" +
            "    // this request is cross-origin; a credentialed one can't be answered with the relay's \"Access-Control-Allow-Origin: *\".\n" +
            "    // Same-origin pages (the normal case) still send cookies.\n" +
            "    credentials: 'same-origin',",
            "credentials: 'same-origin',");

        // The bundle is built against the dotnet-runtime folder beside Web.JS, not a published package.
        PatchWebJSFile(Path.Combine(webJS, "package.json"),
            "\"@microsoft/dotnet-runtime\": \"*\"",
            "\"@microsoft/dotnet-runtime\": \"file:../dotnet-runtime\"",
            "\"@microsoft/dotnet-runtime\": \"file:../dotnet-runtime\"");

        // Imported by the circuit code; added here when the upstream tree does not carry it, so the bundle builds.
        string reconnectEvent = Path.Combine(webJS, "src", "Platform", "Circuits", "ReconnectStateChangedEvent.ts");
        if (File.Exists(reconnectEvent))
        {
            Console.WriteLine($"  ok (upstream has it): {Path.GetFileName(reconnectEvent)}");
            ChangeReport.Patches.Add((Path.GetFileName(reconnectEvent), "not needed, upstream has the file", false));
        }
        else
        {
            File.WriteAllText(reconnectEvent,
                "// Licensed to the .NET Foundation under one or more agreements.\n" +
                "// The .NET Foundation licenses this file to you under the MIT license.\n" +
                "\n" +
                "export interface ReconnectStateChangedEvent {\n" +
                "  state: 'show' | 'hide' | 'retrying' | 'failed' | 'resume-failed' | 'paused' | 'rejected';\n" +
                "  currentAttempt?: number;\n" +
                "  secondsToNextAttempt?: number;\n" +
                "  remote?: boolean;\n" +
                "}\n");
            Console.WriteLine($"  added: {Path.GetFileName(reconnectEvent)}");
            ChangeReport.Patches.Add((Path.GetFileName(reconnectEvent), "added", false));
        }
    }

    /// <summary>Replaces <paramref name="upstream"/> with <paramref name="ours"/> in one file, keeping the file's line endings.</summary>
    static void PatchWebJSFile(string path, string upstream, string ours, string appliedMarker)
    {
        string name = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            WarnPatch($"PATCH NOT APPLIED: {name} is not in the upstream tree any more ({path}).");
            ChangeReport.Patches.Add((name, "NOT APPLIED, the file is no longer in the upstream tree", true));
            return;
        }

        string text = File.ReadAllText(path);
        if (text.Contains(upstream, StringComparison.Ordinal))
        {
            string newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            File.WriteAllText(path, text.Replace(upstream, ours.Replace("\n", newline), StringComparison.Ordinal));
            Console.WriteLine($"  patched: {name}");
            ChangeReport.Patches.Add((name, "applied", false));
        }
        else if (text.Contains(appliedMarker, StringComparison.Ordinal))
        {
            Console.WriteLine($"  ok (already patched): {name}");
            ChangeReport.Patches.Add((name, "already in place", false));
        }
        else
        {
            WarnPatch($"PATCH NOT APPLIED: {name} no longer contains the upstream text this patch replaces: {upstream.Trim()}");
            ChangeReport.Patches.Add((name, "NOT APPLIED, upstream changed the text it replaces", true));
        }
    }

    static void WarnPatch(string message)
    {
        var color = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("  " + message);
        Console.WriteLine("  Upstream changed here. Look at the file and update ApplyWebJSPatches before committing the result.");
        Console.ForegroundColor = color;
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

            // The build folder holds the .props and .targets of Microsoft's own package; this repository's packages have none.
            if (relativePath.Replace('\\', '/').StartsWith("build/", StringComparison.OrdinalIgnoreCase))
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
                if (!editor.HasContent)
                {
                    // Nothing left after the edits (an AssemblyInfo.cs that only served upstream's unit tests).
                    skippedCount++;
                    continue;
                }

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
}