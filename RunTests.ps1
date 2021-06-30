# Start with a clean solution

Get-ChildItem .\ -include bin,obj,publish -Recurse | remove-item -Force -Recurse 
Get-ChildItem ..\RemoteBlazorWebViewTutorial -include bin,obj,publish -Exclude EBWebView -Recurse -Force |  remove-item -Exclude EBWebView -Force -Recurse
Get-ChildItem ${env:HOMEPATH}\.nuget\packages\Peak* | remove-item -Force -Recurse

dotnet publish .\src\RemoteableWebWindowSite -o src\RemoteableWebWindowSite\publish
dotnet publish ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\publish
dotnet publish ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\publish

dotnet build RemoteBlazorWebView.sln


dotnet test testassets\NUnitTestProject\WebDriverTestProject.csproj