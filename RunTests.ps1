# Set the build env to use project references instead of packages
$env:EnvBuildMode = 'Developer' # Developer or Release

# Start with a clean solution

Get-ChildItem .\src -Exclude RemoteWebView.Blazor.JS | Get-ChildItem -include bin,obj,publish,publishNoAuth,publishAuth,artifacts   -Recurse | ForEach-Object ($_) { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem ..\RemoteBlazorWebViewTutorial\ -include bin,obj,publish,publishEmbedded, embedded -Exclude EBWebView -Recurse -Force | ForEach-Object ($_) { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem ${env:HOMEPATH}\.nuget\packages\Peak* | remove-item -Force -Recurse

# Publish the web site server
dotnet publish -c NoAuthorization --self-contained true -r win-x64 .\src\RemoteWebViewService -o src\RemoteWebViewService\bin\publishNoAuth

dotnet publish -c Authorization --self-contained true -r win-x64 .\src\RemoteWebViewService -o src\RemoteWebViewService\bin\publishAuth

dotnet build -c Release .\src\RemoteWebViewService
dotnet tool uninstall PeakSWC.RemoteWebViewService -g

if ($env:EnvBuildMode -eq 'Debug') {
	# remove the cached version!!
	$file = Join-Path $env:HomePath '.dotnet\tools\RemoteWebViewService.exe' 
	if (Test-Path $file) {
		Remove-Item $file
	}

	$file = Join-Path $env:HomePath '.dotnet\tools\.store\peakswc.RemoteWebViewService' 
	if (Test-Path $file) {
		Remove-Item $file -Recurse
	}
	dotnet tool update -g --add-source artifacts PeakSWC.RemoteWebViewService --version 6.*-* 
} else {
	dotnet tool update -g  PeakSWC.RemoteWebViewService --version 6.*-* 
}

# Publish WinFormsApp
dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publish

# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publish\* -Exclude *.exe, wwwroot

# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\embedded\wwwroot


# Publish using the embedded files generated from the previous publish step
dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publishEmbedded

# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publishEmbedded\* -Exclude *.exe -Recurse

# Build the Debug version of both wpf and winforms
dotnet build -c Debug ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.sln

## Same for the wpf app

dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish

# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish\* -Exclude *.exe, wwwroot 

# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\embedded\wwwroot

# Publish using the embedded files generated from the previous publish step
dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publishEmbedded

# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publishEmbedded\* -Exclude *.exe -Recurse

dotnet test testassets\NUnitTestProject\WebDriverTestProject.csproj --logger:"html;LogFileName=logFile.html" 

Invoke-Expression testassets\NUnitTestProject\TestResults\logFile.html

# zip up files for github
$compress = @{
  Path = "src\RemoteWebViewService\bin\publishNoAuth\RemoteWebViewService.exe", "..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publishEmbedded\RemoteBlazorWebViewTutorial.WinFormsApp.exe","..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publishEmbedded\RemoteBlazorWebViewTutorial.WpfApp.exe", "README.txt"
  CompressionLevel = "Fastest"
  DestinationPath = "artifacts\Release.Zip"
}

if ($env:EnvBuildMode -eq 'Release') {
	Compress-Archive @compress -Force
}
