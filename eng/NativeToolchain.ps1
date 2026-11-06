# Locate the MSVC linker and Windows SDK libraries for a Native AOT publish.
# findvcvarsall.bat in the ILCompiler package redirects only stdout of vcvarsall.bat.
# Its stderr ("The system cannot find the file specified.") is concatenated by MSBuild
# with ';' onto the linker directory. These helpers keep diagnostic text out of the path.

function Get-RunTestsLinkerDirectoriesFromOutput {
    param([string[]] $Lines)

    $found = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($Lines)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        foreach ($piece in ($line -split ';')) {
            $text = $piece.Trim().Trim('"').TrimEnd('#').Trim()
            if ($text -match '(?i)^([A-Za-z]:\\.*)\\link\.exe$') {
                $found.Add($Matches[1].TrimEnd('\'))
            }
            elseif ($text -match '(?i)^[A-Za-z]:\\.*\\Hostx64\\x64$') {
                $found.Add($text.TrimEnd('\'))
            }
        }
    }

    return @($found | Sort-Object -Unique)
}

function Resolve-RunTestsNativeToolchainPaths {
    param(
        [Parameter(Mandatory = $true)][string] $MsvcToolsRoot,
        [Parameter(Mandatory = $true)][string] $WindowsKitsLibRoot
    )

    $links = @()
    if (Test-Path -LiteralPath $MsvcToolsRoot) {
        $links = @(Get-ChildItem -LiteralPath $MsvcToolsRoot -Filter link.exe -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { ($_.FullName -replace '\\','/') -match '(?i)/bin/Hostx64/x64/link\.exe$' })
    }
    if ($links.Count -eq 0) {
        return [pscustomobject]@{
            Ok = $false
            Linker = $null
            LinkerDirectory = $null
            LibPaths = @()
            Message = "Native AOT needs MSVC link.exe from the Desktop development with C++ workload. None was found under '$MsvcToolsRoot'. The publish was not started. https://aka.ms/nativeaot-prerequisites"
        }
    }

    $link = $links | Sort-Object -Property @{
        Expression = {
            if (($_.FullName -replace '\\','/') -match '(?i)/MSVC/([^/]+)/') {
                try { return [version]$Matches[1] } catch { return [version]'0.0' }
            }
            return [version]'0.0'
        }
    } -Descending | Select-Object -First 1

    $sdkLibs = @()
    if (Test-Path -LiteralPath $WindowsKitsLibRoot) {
        $sdkLibs = @(Get-ChildItem -LiteralPath $WindowsKitsLibRoot -Filter advapi32.lib -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { ($_.FullName -replace '\\','/') -match '(?i)/um/x64/advapi32\.lib$' })
    }
    if ($sdkLibs.Count -eq 0) {
        return [pscustomobject]@{
            Ok = $false
            Linker = $link.FullName
            LinkerDirectory = $link.DirectoryName
            LibPaths = @()
            Message = "MSVC link.exe is at '$($link.FullName)', but the Windows SDK library advapi32.lib was not found under '$WindowsKitsLibRoot'. Native AOT needs the Windows SDK as well as the C++ workload. The publish was not started. https://aka.ms/nativeaot-prerequisites"
        }
    }

    $sdkLib = $sdkLibs | Sort-Object -Property @{
        Expression = {
            if (($_.FullName -replace '\\','/') -match '(?i)/Lib/([^/]+)/um/x64/') {
                try { return [version]$Matches[1] } catch { return [version]'0.0' }
            }
            return [version]'0.0'
        }
    } -Descending | Select-Object -First 1

    $sdkVersionDir = $sdkLib.Directory.Parent.Parent.FullName
    $ucrt = Join-Path $sdkVersionDir (Join-Path 'ucrt' 'x64')
    if (-not (Test-Path -LiteralPath (Join-Path $ucrt 'ucrt.lib'))) {
        return [pscustomobject]@{
            Ok = $false
            Linker = $link.FullName
            LinkerDirectory = $link.DirectoryName
            LibPaths = @()
            Message = "The Windows SDK at '$sdkVersionDir' has advapi32.lib but not ucrt.lib. The publish was not started. https://aka.ms/nativeaot-prerequisites"
        }
    }

    $msvcVersionDir = $link.Directory.Parent.Parent.Parent.FullName
    $msvcLib = Join-Path $msvcVersionDir (Join-Path 'lib' 'x64')
    return [pscustomobject]@{
        Ok = $true
        Linker = $link.FullName
        LinkerDirectory = $link.DirectoryName
        LibPaths = @($msvcLib, $ucrt, $sdkLib.DirectoryName)
        Message = $null
    }
}

function Resolve-RunTestsNativeToolchain {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere)) {
        return [pscustomobject]@{
            Ok = $false
            Linker = $null
            LinkerDirectory = $null
            LibPaths = @()
            Message = "vswhere.exe was not found at '$vswhere'. Native AOT needs the Desktop development with C++ workload. The publish was not started. https://aka.ms/nativeaot-prerequisites"
        }
    }

    $installLines = @(& $vswhere -latest -prerelease -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>&1 |
        ForEach-Object { "$_" })
    $install = $null
    foreach ($line in $installLines) {
        $text = $line.Trim()
        if ($text -match '^[A-Za-z]:\\' -and (Test-Path -LiteralPath $text)) { $install = $text }
    }
    if (-not $install) {
        return [pscustomobject]@{
            Ok = $false
            Linker = $null
            LinkerDirectory = $null
            LibPaths = @()
            Message = "vswhere did not return a Visual Studio installation with the MSVC toolset. Install the Desktop development with C++ workload. The publish was not started. https://aka.ms/nativeaot-prerequisites"
        }
    }

    return Resolve-RunTestsNativeToolchainPaths -MsvcToolsRoot (Join-Path $install 'VC\Tools\MSVC') -WindowsKitsLibRoot (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Lib')
}

# Component IDs from the Visual Studio Build Tools workload page for the current
# (Visual Studio 2026 / version 18) and 2022 tables:
# https://learn.microsoft.com/en-us/visualstudio/install/workload-component-id-vs-build-tools?view=visualstudio
$script:RunTestsMsvcComponentId = 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
$script:RunTestsWindowsSdkComponentId = 'Microsoft.VisualStudio.Component.Windows11SDK.26100'

function Get-RunTestsAotComponentsToAdd {
    param($Toolchain)
    if ($Toolchain.Ok) { return @() }
    if ($Toolchain.Linker) { return @($script:RunTestsWindowsSdkComponentId) }
    return @($script:RunTestsMsvcComponentId, $script:RunTestsWindowsSdkComponentId)
}

function Get-RunTestsInstallerOutcome {
    param([int] $ExitCode)
    switch ($ExitCode) {
        0 { return 'Success' }
        3010 { return 'RebootRequired' }
        1641 { return 'RebootStarted' }
        1602 { return 'Cancelled' }
        5004 { return 'Cancelled' }
        1618 { return 'AnotherInstall' }
        740 { return 'ElevationRequired' }
        1001 { return 'InstallerBusy' }
        1003 { return 'ProductInUse' }
        -1073741510 { return 'Terminated' }
        default { return 'Failed' }
    }
}

function Test-RunTestsMicrosoftSigner {
    param([string] $Status, [string] $Subject)
    return ($Status -eq 'Valid' -and $Subject -match 'O=Microsoft Corporation')
}

function Get-RunTestsBuildToolsBootstrapperUrl {
    param([string] $InstallPath)
    # aka.ms links checked against download.visualstudio.microsoft.com.
    # 18/stable is the Visual Studio 2026 Build Tools bootstrapper. 17/release is Visual Studio 2022.
    if ($InstallPath -and ($InstallPath -match '\\2022\\|\\17\\')) {
        return 'https://aka.ms/vs/17/release/vs_buildtools.exe'
    }
    return 'https://aka.ms/vs/18/stable/vs_buildtools.exe'
}

function Get-RunTestsInstancePathFromLinker {
    param([string] $Linker)
    if (-not $Linker) { return $null }
    $current = Split-Path -Parent $Linker
    while ($current) {
        $normalized = $current -replace '\\', '/'
        if ($normalized -match '/VC$' -and (Test-Path -LiteralPath (Join-Path $current (Join-Path 'Tools' 'MSVC')))) {
            return Split-Path -Parent $current
        }
        $parent = Split-Path -Parent $current
        if (-not $parent -or $parent -eq $current) { break }
        $current = $parent
    }
    return $null
}

function Install-RunTestsAotPrerequisites {
    param(
        [Parameter(Mandatory = $true)][scriptblock] $Detect,
        [Parameter(Mandatory = $true)][scriptblock] $RunInstaller
    )

    $before = & $Detect
    if ($before.Ok) {
        return [pscustomobject]@{
            Ok = $true
            Installed = $false
            Toolchain = $before
            Message = 'Native AOT prerequisites are already installed (MSVC link.exe and Windows SDK libraries).'
        }
    }

    $components = @(Get-RunTestsAotComponentsToAdd $before)
    Write-Host "Native AOT prerequisites are incomplete. $($before.Message)"
    Write-Host "The official Visual Studio Build Tools installer will add only: $($components -join ', ')."
    Write-Host 'A progress window opens for that installer. Windows may ask you to approve administrator permission for the installer. This script does not elevate itself.'

    $exitCode = [int](& $RunInstaller $components)
    $outcome = Get-RunTestsInstallerOutcome $exitCode
    if ($outcome -eq 'RebootRequired' -or $outcome -eq 'RebootStarted') {
        return [pscustomobject]@{
            Ok = $false
            NeedsReboot = $true
            ExitCode = $exitCode
            Message = "The Visual Studio installer completed and Windows must reboot before the SDK can be used (exit $exitCode). Restart the computer, then run RunTests.ps1 again. The build was not continued."
        }
    }
    if ($outcome -eq 'Cancelled' -or $outcome -eq 'Terminated') {
        return [pscustomobject]@{
            Ok = $false
            ExitCode = $exitCode
            Message = "The Visual Studio installer was canceled (exit $exitCode). Nothing further was built. Run RunTests.ps1 again to retry."
        }
    }
    if ($outcome -ne 'Success') {
        return [pscustomobject]@{
            Ok = $false
            ExitCode = $exitCode
            Message = "The Visual Studio installer did not finish ($outcome, exit $exitCode). Logs are the newest dd_bootstrapper, dd_client, and dd_setup files in $env:TEMP. The build was not continued."
        }
    }

    $after = & $Detect
    if (-not $after.Ok) {
        return [pscustomobject]@{
            Ok = $false
            ExitCode = $exitCode
            Message = "The Visual Studio installer exited 0, but the Native AOT libraries are still incomplete. $($after.Message)"
        }
    }
    return [pscustomobject]@{
        Ok = $true
        Installed = $true
        Toolchain = $after
        Message = 'Native AOT prerequisites are installed.'
    }
}

function Ensure-RunTestsAotPrerequisites {
    $result = Install-RunTestsAotPrerequisites -Detect { Resolve-RunTestsNativeToolchain } -RunInstaller {
        param($components)
        Invoke-RunTestsVisualStudioInstaller -Components $components
    }
    if (-not $result.Ok) {
        Write-Error $result.Message
        exit 1
    }
    Write-Host $result.Message
}

function Invoke-RunTestsVisualStudioInstaller {
    param([string[]] $Components)

    $detected = Resolve-RunTestsNativeToolchain
    $installPath = Get-RunTestsInstancePathFromLinker -Linker $detected.Linker
    $url = Get-RunTestsBuildToolsBootstrapperUrl -InstallPath $installPath
    $bootstrapper = Join-Path $env:TEMP 'vs_buildtools.exe'
    Write-Host "Downloading the Visual Studio Build Tools bootstrapper from $url"
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $url -OutFile $bootstrapper -UseBasicParsing
    } catch {
        Write-Error "Could not download the Visual Studio Build Tools bootstrapper from ${url}: $($_.Exception.Message)"
        exit 1
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
    $subject = ''
    if ($signature.SignerCertificate) { $subject = $signature.SignerCertificate.Subject }
    if (-not (Test-RunTestsMicrosoftSigner -Status ([string]$signature.Status) -Subject $subject)) {
        Write-Error "Refusing to run '$bootstrapper'. Authenticode status is '$($signature.Status)' and signer is '$subject'. Expected a valid Microsoft Corporation signature."
        exit 1
    }

    $argumentList = @()
    if ($installPath) { $argumentList += @('modify', '--installPath', $installPath) }
    foreach ($component in $Components) { $argumentList += @('--add', $component) }
    $argumentList += @('--passive', '--norestart', '--wait')
    Write-Host "Running signed vs_buildtools.exe $($argumentList -join ' ')"
    $process = Start-Process -FilePath $bootstrapper -ArgumentList $argumentList -Wait -PassThru
    return $process.ExitCode
}
