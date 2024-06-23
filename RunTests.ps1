
 Param(
        [string] $Mode = 'Developer',
        [switch] $Build = $false,
		[switch] $Rust = $false
    )

# .\RunTests.ps1  -Build:$true
# .\RunTests.ps1  -Mode Release

# Set the build env to use project references instead of packages
$env:EnvBuildMode = $Mode # Developer or Release

Write-Host -ForegroundColor GREEN ("Build:",$Build)
Write-Host -ForegroundColor GREEN ("Rust:",$Rust)
Write-Host -ForegroundColor GREEN ("EnvBuildMode:",$env:EnvBuildMode)

# Get the path of the script's directory
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Append the relative path to the script's directory
$relativePath = "src\RemoteWebView.Blazor.JS\protoc\bin"
$newPath = Join-Path -Path $scriptDir -ChildPath $relativePath

# Check if the newPath is already in the Path environment variable
$paths = [System.Environment]::GetEnvironmentVariable("Path", [System.EnvironmentVariableTarget]::User) -split ";"
$alreadyExists = $paths.Contains($newPath)

if (-not $alreadyExists) {
    # Add newPath to the Path environment variable
    $newPathVariable = [System.Environment]::GetEnvironmentVariable("Path", [System.EnvironmentVariableTarget]::User) + ";$newPath"
    [System.Environment]::SetEnvironmentVariable("Path", $newPathVariable, [System.EnvironmentVariableTarget]::User)
    Write-Host "Path added: $newPath"
} else {
    Write-Host "Path already exists: $newPath"
}


Write-Host -ForegroundColor GREEN "Clean artifacts"
# Start with a clean solution

Get-ChildItem .\src\RemoteWebView.Blazor.JS -include node_modules,dist -Recurse | ForEach-Object ($_) { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem .\src -Exclude RemoteWebView.Blazor.JS -Directory | ForEach-Object { Get-ChildItem $_.FullName -Include bin,obj,publish,publishNoAuth,publishAuth,artifacts -Recurse} | ForEach-Object { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem ..\RemoteBlazorWebViewTutorial\ -include bin,obj,publish,publishEmbedded, embedded -Exclude EBWebView -Recurse -Force | ForEach-Object ($_) { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem ${env:HOMEPATH}\.nuget\packages\Peak* | remove-item -Force -Recurse

Write-Host -ForegroundColor GREEN "Install node_modules"

$currentDirectory = Get-Location
Set-Location .\src\RemoteWebView.Blazor.JS
yarn install
Set-Location .\Web.JS
yarn install
Set-Location $currentDirectory

Write-Host -ForegroundColor GREEN "Install dotnet-runtime"
# Define the source and destination directories
$sourceDir = "src/RemoteWebView.Blazor.JS/dotnet-runtime"
$destDir = "src/RemoteWebView.Blazor.JS/Web.JS/node_modules"

# Check if the destination directory exists, if not, create it
if (-not (Test-Path -Path $destDir)) {
    New-Item -ItemType Directory -Path $destDir
}

# Copy all files and directories from source to destination
Copy-Item -Path "$sourceDir\*" -Destination $destDir -Recurse -Force

Write-Host -ForegroundColor GREEN "Build remote.blazor.desktop.js"
Set-Location .\src\RemoteWebView.Blazor.JS
if ($env:EnvBuildMode -eq 'Debug') {
	npm run build:debug
}
else {
	npm run build:production
}

Set-Location $currentDirectory

if ($Rust -eq $true)
{
	 Write-Host -ForegroundColor GREEN "Build Release http_to_grpc_bridge"
	 Set-Location .\..\http_to_grpc_bridge
	 cargo b --release
	 cargo build --release
     if (-not $?)
     {
        Write-Host -ForegroundColor Red "Cargo build failed. Exiting..."
        exit 1
     } else
	 {
		Write-Host -ForegroundColor Green "http_to_grpc_bridge Build succeeded. Copying executable..."
        $sourcePath = ".\target\release\http_to_grpc_bridge.exe"
        $destinationPath = "$currentDirectory\..\http_to_grpc_bridge\http_to_grpc_bridge.exe"
        
        # Copy the file, allowing overwrite
        Copy-Item -Path $sourcePath -Destination $destinationPath -Force
        
        Write-Host -ForegroundColor Green "Copy completed."
	 }
	 Set-Location $currentDirectory
	 $env:Rust = "true"
} else
{
    Write-Host -ForegroundColor GREEN "Publish RemoteWebViewService"
	# Publish the web site server
	dotnet publish -c NoAuthorization --self-contained true -r win-x64 .\src\RemoteWebViewService -o src\RemoteWebViewService\bin\publishNoAuth
	dotnet publish -c Authorization --self-contained true -r linux-x64 .\src\RemoteWebViewService -o src\RemoteWebViewService\bin\publishAuth

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
		dotnet tool update -g --add-source artifacts PeakSWC.RemoteWebViewService --version 8.*-* 
	} else {
		dotnet tool update -g  PeakSWC.RemoteWebViewService --version 8.*-* 
	}

}



Write-Host -ForegroundColor GREEN "Publish WinFormsApp"
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

Write-Host -ForegroundColor GREEN "Publish WpfApp"
dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish

# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish\* -Exclude *.exe, wwwroot 

# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\embedded\wwwroot

# Publish using the embedded files generated from the previous publish step
dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publishEmbedded

# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publishEmbedded\* -Exclude *.exe -Recurse

# Same for WebView app
Write-Host -ForegroundColor GREEN "Publish WebView App"
dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publish
# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish\* -Exclude *.exe, wwwroot 
# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\embedded\wwwroot
# Publish using the embedded files generated from the previous publish step
dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publishEmbedded
# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publishEmbedded\* -Exclude *.exe -Recurse

if ($Build -ne $true)
{
	dotnet test testassets\NUnitTestProject\WebDriverTestProject.csproj --logger:"html;LogFileName=logFile.html" 
	Invoke-Expression testassets\NUnitTestProject\TestResults\logFile.html
}

# zip up files for github
$compress = @{
  Path = "src\RemoteWebViewService\bin\publishNoAuth\RemoteWebViewService.exe", "..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publishEmbedded\RemoteBlazorWebViewTutorial.WinFormsApp.exe","..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publishEmbedded\RemoteBlazorWebViewTutorial.WpfApp.exe", "README.txt"
  CompressionLevel = "Fastest"
  DestinationPath = "artifacts\Release.Zip"
}

if ($env:EnvBuildMode -eq 'Release') {
	Compress-Archive @compress -Force
}
