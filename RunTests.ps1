# Set the build env to use project references instead of packages
$env:EnvBuildMode = 'Development'

# Start with a clean solution

Get-ChildItem .\ -include bin,obj,publish,publishNoAuth,publishAuth -Recurse | ForEach-Object ($_) { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem ..\RemoteBlazorWebViewTutorial\ -include bin,obj,publish,publishEmbedded, embedded -Exclude EBWebView -Recurse -Force | ForEach-Object ($_) { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem ${env:HOMEPATH}\.nuget\packages\Peak* | remove-item -Force -Recurse

# Publish the web site server
dotnet publish -c NoAuthorization --self-contained true -r win-x64 .\src\RemoteableWebViewService -o src\RemoteableWebViewService\bin\publishNoAuth
Copy-Item .\src\RemoteableWebViewService\appsettings.Development.json src\RemoteableWebViewService\bin\publishNoAuth\appsettings.json

dotnet publish -c Authorization --self-contained true -r win-x64 .\src\RemoteableWebViewService -o src\RemoteableWebViewService\bin\publishAuth

dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\publish

# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\publish\* -Exclude *.exe, wwwroot

# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\embedded\wwwroot


# Publish using the embedded files generated from the previous publish step
dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\publishEmbedded

# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\publishEmbedded\* -Exclude *.exe -Recurse


## Same for the wpf app

dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\publish

# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\publish\* -Exclude *.exe, wwwroot 


# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\embedded\wwwroot

# Publish using the embedded files generated from the previous publish step
dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\publishEmbedded

# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\publishEmbedded\* -Exclude *.exe -Recurse

#
# Debug mode is not working in Preview7
#dotnet build -c Debug RemoteBlazorWebView.sln


dotnet test testassets\NUnitTestProject\WebDriverTestProject.csproj --logger:"html;LogFileName=logFile.html" 

Invoke-Expression testassets\NUnitTestProject\TestResults\logFile.html