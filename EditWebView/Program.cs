﻿// See https://aka.ms/new-console-template for more information
using EditWebView;

string maui = "maui-6.0.200-preview.12";
string inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\WindowsForms");
string outputDir = "../../../../src/RemoteBlazorWebView.WinForms";

ProcessFiles(inputDir, outputDir);

inputDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", maui, @"src\BlazorWebView\src\Wpf");
outputDir = "../../../../src/RemoteBlazorWebView.Wpf";

ProcessFiles(inputDir, outputDir);

static void ProcessFiles(string inputDir, string outputDir)
{
    if (!Directory.Exists(outputDir))
        throw new Exception("Can't locate output directory");

    foreach (var f in Directory.EnumerateFiles(inputDir))
    {
        if (f.EndsWith(".csproj")) continue;

        Editor editor = new Editor(f);
        editor.Edit();
        editor.WriteAllText(outputDir);
    }
}
