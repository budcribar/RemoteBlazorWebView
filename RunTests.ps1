
 param(
        [string] $Mode = 'Developer',
        [switch] $Build = $false,
		[switch] $Rust = $false
    )

# .\RunTests.ps1  -Build:$true
# .\RunTests.ps1  -Mode Release

# Set the build env to use project references instead of packages
$env:EnvBuildMode = $Mode # Developer or Release
. (Join-Path $PSScriptRoot 'eng\NativeToolchain.ps1')

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

function ConvertTo-RunTestsSdkVersion {
    param([string] $Text)
    if ($Text -notmatch '^(\d+)\.(\d+)\.(\d+)') { return $null }
    [pscustomobject]@{
        Major = [int]$Matches[1]
        Minor = [int]$Matches[2]
        Patch = [int]$Matches[3]
        Raw = $Matches[0]
    }
}

function Test-RunTestsSdkSatisfies {
    param([string] $Requested, [string] $Installed, [string] $RollForward)
    $want = ConvertTo-RunTestsSdkVersion $Requested
    $have = ConvertTo-RunTestsSdkVersion $Installed
    if (-not $want -or -not $have) { return $false }
    $policy = if ($RollForward) { $RollForward } else { 'latestPatch' }
    if ($policy -eq 'disable') { return $have.Raw -eq $want.Raw }
    if ($have.Major -ne $want.Major) {
        return ($policy -in @('major', 'latestMajor') -and $have.Major -gt $want.Major)
    }
    if ($policy -in @('patch', 'latestPatch')) {
        $wantBand = [math]::Floor($want.Patch / 100)
        $haveBand = [math]::Floor($have.Patch / 100)
        return ($have.Minor -eq $want.Minor -and $haveBand -eq $wantBand -and $have.Patch -ge $want.Patch)
    }
    if ($policy -in @('feature', 'latestFeature')) {
        return ($have.Minor -eq $want.Minor -and $have.Patch -ge $want.Patch)
    }
    if ($have.Minor -gt $want.Minor) { return $true }
    if ($have.Minor -lt $want.Minor) { return $false }
    return $have.Patch -ge $want.Patch
}

function Get-RunTestsInstalledSdkVersions {
    $list = & dotnet --list-sdks 2>&1
    if ($LASTEXITCODE -ne 0) { return @() }
    $versions = @()
    foreach ($line in @($list)) {
        if ($line -match '^(\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?)') { $versions += $Matches[1] }
    }
    return $versions
}

function Install-RunTestsSdkIfMissing {
    $globalJson = Join-Path (Get-Location) 'global.json'
    if (-not (Test-Path -LiteralPath $globalJson)) {
        Write-Error "global.json was not found in $(Get-Location). The SDK pin was not changed."
        exit 1
    }
    $sdk = (Get-Content -Raw -LiteralPath $globalJson | ConvertFrom-Json).sdk
    $requested = [string]$sdk.version
    $rollForward = [string]$sdk.rollForward
    foreach ($installed in @(Get-RunTestsInstalledSdkVersions)) {
        if (Test-RunTestsSdkSatisfies -Requested $requested -Installed $installed -RollForward $rollForward) {
            Write-Host "Using installed .NET SDK $installed (global.json requests $requested, rollForward $rollForward)."
            return
        }
    }

    if (-not $env:LOCALAPPDATA) {
        Write-Error ".NET SDK $requested is not installed and LOCALAPPDATA is unset, so a user-local SDK cannot be bootstrapped."
        exit 1
    }
    $installDir = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
    $sdkMarker = Join-Path $installDir "sdk\$requested"
    if (-not (Test-Path -LiteralPath $sdkMarker)) {
        $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
        $installerUrl = 'https://dot.net/v1/dotnet-install.ps1'
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $installerUrl -OutFile $installer -UseBasicParsing
        } catch {
            Write-Error "Could not download the official .NET install script from ${installerUrl}: $($_.Exception.Message)"
            exit 1
        }
        Write-Host "Installing .NET SDK $requested for this user under $installDir. Program Files is not changed."
        & $installer -Version $requested -InstallDir $installDir -NoPath
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $sdkMarker)) {
            Write-Error ".NET SDK $requested was not installed under $installDir. global.json was not changed. Install that SDK, then run this script again."
            exit 1
        }
    }

    $env:DOTNET_ROOT = $installDir
    Update-RunTestsSessionPath -Prepend @($installDir)
    $visible = $false
    foreach ($installed in @(Get-RunTestsInstalledSdkVersions)) {
        if (Test-RunTestsSdkSatisfies -Requested $requested -Installed $installed -RollForward $rollForward) { $visible = $true }
    }
    if (-not $visible) {
        Write-Error ".NET SDK $requested is on disk at $installDir but 'dotnet --list-sdks' still cannot use it."
        exit 1
    }
    Write-Host "Using user-local .NET SDK $requested."
}

function Invoke-RunTestsNative {
    $file = [string]$args[0]
    $rest = @()
    if ($args.Count -gt 1) { $rest = @($args[1..($args.Count - 1)]) }
    & $file @rest
    if ($LASTEXITCODE -ne 0) {
        Write-Host -ForegroundColor Red "$file $($rest -join ' ') failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

Switch-RunTestsNodeVersion
Install-RunTestsSdkIfMissing

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
# Missing output folders are not a failure. A later command that needs them still stops the script.
Get-ChildItem .\src\RemoteWebView.Blazor.JS -Include node_modules,dist -Recurse -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item $_.FullName -Force -Recurse -ErrorAction SilentlyContinue }
Get-ChildItem .\src -Exclude RemoteWebView.Blazor.JS -Directory -ErrorAction SilentlyContinue | ForEach-Object { Get-ChildItem $_.FullName -Include bin,obj,publish,publishNoAuth,publishAuth,artifacts -Recurse -ErrorAction SilentlyContinue } | ForEach-Object { Remove-Item $_.FullName -Force -Recurse -ErrorAction SilentlyContinue }
Get-ChildItem .\test -Directory -ErrorAction SilentlyContinue | ForEach-Object { Get-ChildItem $_.FullName -Include bin,obj -Recurse -ErrorAction SilentlyContinue } | ForEach-Object { Remove-Item $_.FullName -Force -Recurse -ErrorAction SilentlyContinue }
Get-ChildItem ..\RemoteBlazorWebViewTutorial\ -Include bin,obj,publish,publishEmbedded, embedded -Exclude EBWebView -Recurse -Force -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item $_.FullName -Force -Recurse -ErrorAction SilentlyContinue }
Get-ChildItem ${env:HOMEPATH}\.nuget\packages\Peak* -ErrorAction SilentlyContinue | Remove-Item -Force -Recurse -ErrorAction SilentlyContinue

Write-Host -ForegroundColor GREEN "Install node_modules"

$currentDirectory = Get-Location
Set-Location .\src\RemoteWebView.Blazor.JS
# package.json pins yarn 4.3.1. Web.JS is not a Yarn workspace: Yarn 4 refuses
# `yarn install` there. npm installs that package's own dependencies, including
# @microsoft/dotnet-js-interop, @microsoft/signalr, and the file: dotnet-runtime.
Invoke-RunTestsNative corepack enable
Invoke-RunTestsNative corepack prepare yarn@4.3.1 --activate
Invoke-RunTestsNative yarn install --immutable
Set-Location .\Web.JS
Invoke-RunTestsNative npm install --no-audit --no-fund --include=dev
foreach ($module in @('@microsoft/dotnet-js-interop', '@microsoft/signalr', '@microsoft/dotnet-runtime/package.json')) {
    if (-not (Test-Path -LiteralPath (Join-Path 'node_modules' $module))) {
        Write-Error "Web.JS is missing $module after npm install."
        exit 1
    }
}
Set-Location ..

Write-Host -ForegroundColor GREEN "Build remote.blazor.desktop.js"
if ($env:EnvBuildMode -eq 'Debug') {
	Invoke-RunTestsNative npm run build:debug
}
else {
	Invoke-RunTestsNative npm run build:production
}
$bundle = Join-Path (Get-Location) 'dist\remote.blazor.desktop.js'
if (-not (Test-Path -LiteralPath $bundle) -or (Get-Item -LiteralPath $bundle).Length -le 0) {
    Write-Error "remote.blazor.desktop.js was not produced."
    exit 1
}
Write-Host "remote.blazor.desktop.js is $((Get-Item -LiteralPath $bundle).Length) bytes"

Set-Location $currentDirectory

Write-Host -ForegroundColor GREEN "Publish TestClient"
# Quoted so PowerShell does not split -p:PublishSingleFile=true before dotnet sees it.
Invoke-RunTestsNative dotnet publish -c Release --self-contained true -r win-x64 '-p:PublishSingleFile=true' .\src\Benchmarks\FilePOC\Client\Client.csproj -o .\src\Benchmarks\FilePOC\Client\publish


if ($Rust -eq $true)
{
	 Write-Host -ForegroundColor GREEN "Build Release http_to_grpc_bridge"
	 Set-Location .\..\http_to_grpc_bridge
	 Invoke-RunTestsNative cargo b --release
	 Invoke-RunTestsNative cargo build --release
	 Write-Host -ForegroundColor Green "http_to_grpc_bridge Build succeeded. Copying executable..."
     $sourcePath = ".\target\release\http_to_grpc_bridge.exe"
     $destinationPath = "$currentDirectory\..\http_to_grpc_bridge\http_to_grpc_bridge.exe"
     Copy-Item -Path $sourcePath -Destination $destinationPath -Force
     Write-Host -ForegroundColor Green "Copy completed."
	 Set-Location $currentDirectory
	 $env:Rust = "true"
} else
{
    Write-Host -ForegroundColor GREEN "Publish RemoteWebViewService"
	# Publish the web site server
	# NoAuthorization is PublishAot. findvcvarsall.bat folds vcvarsall's stderr into the
	# link.exe path, so locate the linker and Windows SDK here and skip that script.
	$toolchain = Resolve-RunTestsNativeToolchain
	if (-not $toolchain.Ok) {
		Write-Error $toolchain.Message
		exit 1
	}
	$savedPath = $env:Path
	$savedLib = $env:LIB
	$env:Path = "$($toolchain.LinkerDirectory);$env:Path"
	$env:LIB = ($toolchain.LibPaths -join ';')
	try {
		Invoke-RunTestsNative dotnet publish -c NoAuthorization --self-contained true -r win-x64 '-p:IlcUseEnvironmentalTools=true' .\src\RemoteWebViewService -o src\RemoteWebViewService\bin\publishNoAuth
	} finally {
		$env:Path = $savedPath
		if ($null -eq $savedLib) { Remove-Item Env:LIB -ErrorAction SilentlyContinue } else { $env:LIB = $savedLib }
	}
    New-Item -Path "src\RemoteWebViewService\bin\publishNoAuth\wwwroot" -ItemType Directory -Force | Out-Null
	Invoke-RunTestsNative dotnet publish -c Authorization --self-contained true -r linux-x64 .\src\RemoteWebViewService -o src\RemoteWebViewService\bin\publishAuth

	Invoke-RunTestsNative dotnet build -c Release .\src\RemoteWebViewService
	dotnet tool uninstall PeakSWC.RemoteWebViewService -g
	if ($LASTEXITCODE -ne 0) {
		Write-Host "PeakSWC.RemoteWebViewService was not installed globally. Continuing to the tool update."
	}

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
		Invoke-RunTestsNative dotnet tool update -g --add-source artifacts PeakSWC.RemoteWebViewService --version 8.*-*
	} else {
		Invoke-RunTestsNative dotnet tool update -g PeakSWC.RemoteWebViewService --version 8.*-*
	}

}




Write-Host -ForegroundColor GREEN "Publish WinFormsApp"
# Publish WinFormsApp
Invoke-RunTestsNative dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publish

# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publish\* -Exclude *.exe, wwwroot

# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\embedded\wwwroot


# Publish using the embedded files generated from the previous publish step
Invoke-RunTestsNative dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publishEmbedded

# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WinFormsApp\bin\publishEmbedded\* -Exclude *.exe -Recurse

# Build the Debug version of both wpf and winforms
Invoke-RunTestsNative dotnet build -c Debug ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.sln

## Same for the wpf app

Write-Host -ForegroundColor GREEN "Publish WpfApp"
Invoke-RunTestsNative dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish

# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish\* -Exclude *.exe, wwwroot 

# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\embedded\wwwroot

# Publish using the embedded files generated from the previous publish step
Invoke-RunTestsNative dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publishEmbedded

# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial.WpfApp\bin\publishEmbedded\* -Exclude *.exe -Recurse

# Same for WebView app
Write-Host -ForegroundColor GREEN "Publish WebView App"
Invoke-RunTestsNative dotnet publish -c Release --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publish
# Delete all files except the executable and wwwroot
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publish\* -Exclude *.exe, wwwroot 
# created the embedded files
Copy-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publish\wwwroot -Recurse ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\embedded\wwwroot
# Publish using the embedded files generated from the previous publish step
Invoke-RunTestsNative dotnet publish -c Embedded --self-contained true -r win-x64 ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial -o ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publishEmbedded
# Delete all files except the executable
Remove-Item ..\RemoteBlazorWebViewTutorial\RemoteBlazorWebViewTutorial\bin\publishEmbedded\* -Exclude *.exe -Recurse

if ($Build -ne $true) {
    # Navigate to the first test directory
    $targetPath = Resolve-Path "test\FileSyncServer.Tests"
    Set-Location $targetPath

    # Microsoft.Playwright 1.64.1 writes playwright.ps1 into the test output during build.
    # Install browsers from that script. There is no global `playwright` command.
    Invoke-RunTestsNative dotnet build FileSyncServer.Tests.csproj -c Release
    $playwright = Get-ChildItem -Path (Join-Path (Get-Location) 'bin') -Recurse -Filter playwright.ps1 -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $playwright) {
        Write-Error "playwright.ps1 was not produced by the FileSyncServer.Tests build."
        exit 1
    }
    Write-Host "Installing Playwright browsers with $($playwright.FullName)"
    Invoke-RunTestsNative $playwright.FullName install

    # Generate a timestamp
    $timestamp = Get-Date -Format "yyyy-MM-dd_HHmmss"

    # Run the first test
    $logfile1 = "FileSyncServerTestLog_$timestamp.html"
    & dotnet test FileSyncServer.Tests.csproj --logger:"html;LogFileName=$logfile1"
    $testExit = $LASTEXITCODE
    if ($testExit -ne 0) {
        Write-Error "dotnet test failed with exit code $testExit. The HTML log was not opened."
        exit $testExit
    }

    $logfilePath1 = Join-Path (Get-Location) "TestResults\$logfile1"
    if (Test-Path -LiteralPath $logfilePath1) {
        Start-Process $logfilePath1
    } else {
        Write-Host "Test log was not produced at $logfilePath1"
    }

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