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
