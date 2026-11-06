
 param(
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

# Check if the process is running
$process = Get-Process -Name RemoteWebViewService -ErrorAction SilentlyContinue

if ($process) {
    Write-Host  -ForegroundColor YELLOW "Stopping RemoteWebViewService"
    Stop-Process -Name RemoteWebViewService -Force
} else {
    Write-Host -ForegroundColor GREEN "RemoteWebViewService is not running"
}

# Check if visual studio is running. It will prevent remote.blazor.desktop.js from building in production mode
$process = Get-Process -Name devenv -ErrorAction SilentlyContinue
if ($process) {
    Write-Host  -ForegroundColor RED "Cannot run tests with Visual Studio running"
    exit 1
}
# Check if the process is running
$process = Get-Process -Name Client -ErrorAction SilentlyContinue

if ($process) {
    Write-Host  -ForegroundColor YELLOW "Stopping Client"
    Stop-Process -Name Client -Force
} else {
    Write-Host -ForegroundColor GREEN "Client is not running"
}

# Node 22.3.0 via nvm-windows, the manager documented in src/RemoteWebView.Blazor.JS/Build.md.
# An existing nvm.exe is kept. A missing one is the official 1.2.2 no-install zip
# (https://github.com/coreybutler/nvm-windows/releases/download/1.2.2/nvm-noinstall.zip),
# checked against that release's published MD5. settings.txt matches install.cmd.
# The symlink stays under LocalAppData so an existing Program Files Node.js install is left alone.
$script:RunTestsNodeVersion = '22.3.0'
$script:RunTestsNvmVersion = '1.2.2'
$script:RunTestsNvmZipMd5 = 'D654C26A04E35A318D5939F8CEB09934'

function Get-RunTestsNodeVersion {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) { return $null }
    return (& node -v 2>$null)
}

function Get-RunTestsNvmExecutable {
    $candidates = @()
    $command = Get-Command nvm -ErrorAction SilentlyContinue
    if ($command -and $command.Source) { $candidates += $command.Source }
    $directories = @()
    if ($env:NVM_HOME) { $directories += $env:NVM_HOME }
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:APPDATA, $env:LOCALAPPDATA)) {
        if ($root) { $directories += (Join-Path $root 'nvm') }
    }
    foreach ($directory in $directories) {
        $candidates += (Join-Path $directory 'nvm.exe')
    }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    return $null
}

function Get-RunTestsNvmSymlink {
    param([string] $NvmExecutable)
    if ($env:NVM_SYMLINK) { return $env:NVM_SYMLINK }
    $settings = Join-Path (Split-Path -Parent $NvmExecutable) 'settings.txt'
    if (Test-Path -LiteralPath $settings) {
        foreach ($line in Get-Content -LiteralPath $settings) {
            if ($line -match '^\s*path:\s*(.+)\s*$') { return $Matches[1].Trim() }
        }
    }
    return $null
}

function Update-RunTestsSessionPath {
    param([string[]] $Prepend)
    $combined = New-Object System.Collections.Generic.List[string]
    foreach ($entry in @($Prepend)) {
        if ($entry -and -not $combined.Contains($entry)) { $combined.Add($entry) }
    }
    foreach ($scope in @('User', 'Machine')) {
        $value = [Environment]::GetEnvironmentVariable('Path', $scope)
        if (-not $value) { continue }
        foreach ($entry in ($value -split ';')) {
            if ($entry -and -not $combined.Contains($entry)) { $combined.Add($entry) }
        }
    }
    $env:Path = ($combined -join ';')
}

function Add-RunTestsUserPathEntry {
    param([string] $Entry)
    if (-not $Entry) { return }
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $items = @()
    if ($userPath) { $items = @($userPath -split ';' | Where-Object { $_ }) }
    if ($items -contains $Entry) { return }
    $updated = ((@($items) + $Entry) -join ';')
    [Environment]::SetEnvironmentVariable('Path', $updated, 'User')
}

function Assert-RunTestsNvmPackage {
    param([string] $Path)
    $hash = (Get-FileHash -Algorithm MD5 -Path $Path).Hash
    if ($hash -ne $script:RunTestsNvmZipMd5) {
        throw "checksum mismatch for nvm-noinstall.zip (expected MD5 $($script:RunTestsNvmZipMd5), got $hash)."
    }
}

function Install-RunTestsNvmIfMissing {
    $existing = Get-RunTestsNvmExecutable
    if ($existing) {
        Write-Host "Using existing nvm: $existing"
        $nvmHome = Split-Path -Parent $existing
        if (-not $env:NVM_HOME) { $env:NVM_HOME = $nvmHome }
        $symlink = Get-RunTestsNvmSymlink -NvmExecutable $existing
        Update-RunTestsSessionPath -Prepend @($nvmHome, $symlink)
        return $existing
    }

    if ($env:NVM_HOME) {
        Write-Error "nvm bootstrap failed: NVM_HOME is '$env:NVM_HOME' but nvm.exe is not there. Existing nvm settings were not replaced."
        exit 1
    }

    $nvmHome = Join-Path $env:LOCALAPPDATA 'nvm'
    $symlink = Join-Path $env:LOCALAPPDATA 'nvm-windows-nodejs'
    if (Test-Path -LiteralPath $nvmHome) {
        Write-Error "nvm bootstrap failed: '$nvmHome' already exists and does not contain nvm.exe. It was not overwritten."
        exit 1
    }
    if (Test-Path -LiteralPath $symlink) {
        Write-Error "nvm bootstrap failed: symlink path '$symlink' already exists. It was not overwritten."
        exit 1
    }

    $zip = Join-Path $env:TEMP ("nvm-noinstall-{0}.zip" -f $script:RunTestsNvmVersion)
    $url = "https://github.com/coreybutler/nvm-windows/releases/download/$($script:RunTestsNvmVersion)/nvm-noinstall.zip"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing
        Assert-RunTestsNvmPackage -Path $zip
    } catch {
        Write-Error "nvm bootstrap failed downloading ${url}: $($_.Exception.Message)"
        exit 1
    }

    New-Item -ItemType Directory -Path $nvmHome | Out-Null
    try {
        Expand-Archive -LiteralPath $zip -DestinationPath $nvmHome -Force
    } catch {
        Write-Error "nvm bootstrap failed extracting ${zip}: $($_.Exception.Message)"
        exit 1
    }

    $installed = Join-Path $nvmHome 'nvm.exe'
    if (-not (Test-Path -LiteralPath $installed)) {
        Write-Error "nvm bootstrap failed: nvm.exe was not in the official $($script:RunTestsNvmVersion) package after extraction."
        exit 1
    }

    $arch = '32'
    if ([Environment]::Is64BitOperatingSystem) { $arch = '64' }
    $settings = @(
        "root: $nvmHome",
        "path: $symlink",
        "arch: $arch",
        "proxy: none"
    )
    Set-Content -LiteralPath (Join-Path $nvmHome 'settings.txt') -Value $settings -Encoding ASCII

    [Environment]::SetEnvironmentVariable('NVM_HOME', $nvmHome, 'User')
    [Environment]::SetEnvironmentVariable('NVM_SYMLINK', $symlink, 'User')
    $env:NVM_HOME = $nvmHome
    $env:NVM_SYMLINK = $symlink
    Add-RunTestsUserPathEntry -Entry $nvmHome
    Add-RunTestsUserPathEntry -Entry $symlink
    Update-RunTestsSessionPath -Prepend @($nvmHome, $symlink)

    Write-Host "Installed nvm-windows $($script:RunTestsNvmVersion) at $nvmHome"
    return (Resolve-Path -LiteralPath $installed).Path
}

function Switch-RunTestsNodeVersion {
    $currentVersion = Get-RunTestsNodeVersion
    if ($currentVersion -eq "v$($script:RunTestsNodeVersion)") {
        Write-Output "Already using Node.js version $($script:RunTestsNodeVersion)"
        return
    }

    $nvm = Install-RunTestsNvmIfMissing
    $listed = (& $nvm list 2>&1 | Out-String)
    if ($listed -notmatch "(^|[^\d.])$([regex]::Escape($script:RunTestsNodeVersion))([^\d.]|$)") {
        & $nvm install $script:RunTestsNodeVersion
        if ($LASTEXITCODE -ne 0) {
            Write-Error "nvm install $($script:RunTestsNodeVersion) failed with exit code $LASTEXITCODE."
            exit 1
        }
    }

    & $nvm use $script:RunTestsNodeVersion
    if ($LASTEXITCODE -ne 0) {
        Write-Error "nvm use $($script:RunTestsNodeVersion) failed with exit code $LASTEXITCODE. nvm-windows recreates the Node.js symlink and may need an elevated session."
        exit 1
    }

    $symlink = Get-RunTestsNvmSymlink -NvmExecutable $nvm
    Update-RunTestsSessionPath -Prepend @((Split-Path -Parent $nvm), $symlink)
    $currentVersion = Get-RunTestsNodeVersion
    if ($currentVersion -ne "v$($script:RunTestsNodeVersion)") {
        Write-Error "Node.js is '$currentVersion' after nvm use $($script:RunTestsNodeVersion)."
        exit 1
    }
    Write-Output "Successfully switched to Node.js version $($script:RunTestsNodeVersion)"
}

Switch-RunTestsNodeVersion

Write-Output "Installing playwright"
npx playwright install
playwright install

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

Get-ChildItem .\src\RemoteWebView.Blazor.JS -Include node_modules,dist -Recurse | ForEach-Object ($_) { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem .\src -Exclude RemoteWebView.Blazor.JS -Directory | ForEach-Object { Get-ChildItem $_.FullName -Include bin,obj,publish,publishNoAuth,publishAuth,artifacts -Recurse} | ForEach-Object { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem .\test -Directory | ForEach-Object { Get-ChildItem $_.FullName -Include bin,obj -Recurse} | ForEach-Object { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem ..\RemoteBlazorWebViewTutorial\ -Include bin,obj,publish,publishEmbedded, embedded -Exclude EBWebView -Recurse -Force | ForEach-Object ($_) { Remove-Item $_.FullName -Force -Recurse }
Get-ChildItem ${env:HOMEPATH}\.nuget\packages\Peak* | Remove-Item -Force -Recurse

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
$destDir = "src/RemoteWebView.Blazor.JS/Web.JS/node_modules/@microsoft/dotnet-runtime"

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

Write-Host -ForegroundColor GREEN "Publish TestClient"
dotnet publish -c Release --self-contained true -r win-x64 -p:PublishSingleFile=true .\src\Benchmarks\FilePOC\Client\Client.csproj -o .\src\Benchmarks\FilePOC\Client\publish


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
    New-Item -Path "src\RemoteWebViewService\bin\publishNoAuth\wwwroot" -ItemType Directory
	dotnet publish -c Authorization --self-contained true -r linux-x64 .\src\RemoteWebViewService -o src\RemoteWebViewService\bin\publishAuth

	dotnet build -c Release .\src\RemoteWebViewService
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
# Non-embedded WPF is multi-file. wwwroot and the mixed-mode assemblies stay
# beside the exe. PublishSingleFile is only used for the Embedded configuration below.
dotnet publish -c Release --self-contained true -r win-x64 -p:PublishSingleFile=false ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish

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
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publish\* -Exclude *.exe, wwwroot 
# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\embedded\wwwroot
# Publish using the embedded files generated from the previous publish step
dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publishEmbedded
# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publishEmbedded\* -Exclude *.exe -Recurse

if ($Build -ne $true) {
    # Navigate to the first test directory
    $targetPath = Resolve-Path "test\FileSyncServer.Tests"
    Set-Location $targetPath

    # Generate a timestamp
    $timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"

    # Run the first test
    $logfile1 = "FileSyncServerTestLog_$timestamp.html"
    dotnet test FileSyncServer.Tests.csproj --logger:"html;LogFileName=$logfile1"
    
    # Open the log file in the default browser
    $logfilePath1 = Resolve-Path "TestResults\$logfile1"
    Start-Process $logfilePath1

    # Return to the original directory
    Set-Location $currentDirectory
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
