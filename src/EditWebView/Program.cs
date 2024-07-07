// See https://aka.ms/new-console-template for more information
using EditWebView;

string aspNetUrl = "https://github.com/dotnet/aspnetcore/archive/refs/tags/v8.0.6.zip";
string mauiUrl = "https://github.com/dotnet/maui/archive/refs/tags/8.0.61.zip";

string url = aspNetUrl;

string zipFile = Utility.GetAspNetCoreFilenameFromUrl(url);

string destinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", zipFile);
string destinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

if (!File.Exists(destinationPath))
    await Utility.DownloadZipFileAsync(url, destinationPath);

if(!Path.Exists(Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(destinationPath))))
    Utility.UnzipFile(destinationPath, destinationFolder);

var webJSTarget = Path.Combine(@"../../../../../", @"RemoteWebView.Blazor.JS/Web.JS");

// delete the WebJS directory
Utility.DeleteDirectoryAndContents(webJSTarget);


var webJSource = Path.Combine(Path.Combine(destinationPath.Replace(".zip",""), @"src/components/Web.JS"));
// copy the WebJS directory
Utility.CopyDirectory(webJSource, webJSTarget);

url = mauiUrl;
zipFile = Path.GetFileNameWithoutExtension(url);
destinationPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", zipFile);
destinationFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
if (!File.Exists(destinationPath))
    await Utility.DownloadZipFileAsync(url, destinationPath);

if (!Path.Exists(Path.Combine(destinationFolder, Utility.ExtractVersionFromPath(destinationPath))))
    Utility.UnzipFile(destinationPath, destinationFolder);

string maui = "maui-" + Utility.ExtractVersionFromPath(destinationPath);
string inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\WindowsForms");
string outputDir = "../../../../../RemoteBlazorWebView.WinForms";

ProcessFiles(inputDir, outputDir);

inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\Wpf");
outputDir = "../../../../../RemoteBlazorWebView.Wpf";

ProcessFiles(inputDir, outputDir);

inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\SharedSource");
outputDir = "../../../../../SharedSource";

ProcessFiles(inputDir, outputDir);

var updated = Utility.GetOutOfDateFiles(Path.Combine(Directory.GetCurrentDirectory(), "../../../../../../"));

foreach (var file in updated)
{
    Utility.ConvertUnixToWindowsLineEndings(Path.Combine("../../../../../../", file));
    Utility.ConvertUtf8ToWindows1252(Path.Combine("../../../../../../", file));
}
   

static void ProcessFiles(string inputDir, string outputDir)
{
    if (!Directory.Exists(outputDir))
        throw new Exception("Can't locate output directory");

    foreach (var f in Directory.EnumerateFiles(inputDir))
    {
        if (f.EndsWith(".csproj")) continue;
        //if (f.Contains("UrlLoadingEvent")) continue;

        Editor editor = new(f);
        editor.Edit();
        editor.WriteAllText(outputDir);
    }
}
