// See https://aka.ms/new-console-template for more information
using EditWebView;

string maui = "maui-6.0.101-preview.11.3";
string inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\WindowsForms");
string outputDir = "../../../../src/RemoteBlazorWebView.WinForms";

if (!Directory.Exists(outputDir))
    throw new Exception("Can't locate output directory");

foreach(var f in Directory.EnumerateFiles(inputDir))
{
    if (f.EndsWith(".csproj")) continue;

    Editor editor = new Editor(f);
    editor.Edit();  
    editor.WriteAllText(outputDir);
}

inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\Wpf");
outputDir = "../../../../src/RemoteBlazorWebView.Wpf";

if (!Directory.Exists(outputDir))
    throw new Exception("Can't locate output directory");

foreach (var f in Directory.EnumerateFiles(inputDir))
{
    if (f.EndsWith(".csproj")) continue;

    Editor editor = new Editor(f);
    editor.Edit();
    editor.WriteAllText(outputDir);
}
