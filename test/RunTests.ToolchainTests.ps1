$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\eng\NativeToolchain.ps1')

function Assert-Equal($actual, $expected, [string] $label) {
    if ($actual -ne $expected) { throw "$label : expected '$expected' but was '$actual'" }
}

$joined = 'The system cannot find the file specified.;C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC\14.50.35717\bin\Hostx64\x64\link.exe'
$directories = @(Get-RunTestsLinkerDirectoriesFromOutput -Lines @($joined))
Assert-Equal $directories.Count 1 'diagnostic prefix count'
Assert-Equal $directories[0] 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC\14.50.35717\bin\Hostx64\x64' 'diagnostic prefix path'

$separate = @(
    'The system cannot find the file specified.',
    'The filename, directory name, or volume label syntax is incorrect.',
    'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC\14.44.35207\bin\Hostx64\x64#',
    'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC\14.50.35717\bin\Hostx64\x64\link.exe'
)
$many = @(Get-RunTestsLinkerDirectoriesFromOutput -Lines $separate)
Assert-Equal $many.Count 2 'multiple linker count'
if ($many -contains 'The system cannot find the file specified.') { throw 'diagnostic text was treated as a linker directory' }

$root = Join-Path ([System.IO.Path]::GetTempPath()) ('toolchain-' + [guid]::NewGuid().ToString('n'))
$msvc = Join-Path $root 'Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Tools\MSVC'
$oldLink = Join-Path $msvc '14.44.35207\bin\Hostx64\x64\link.exe'
$newLink = Join-Path $msvc '14.50.35717\bin\Hostx64\x64\link.exe'
$kits = Join-Path $root 'Program Files (x86)\Windows Kits\10\Lib'
$oldSdk = Join-Path $kits '10.0.22621.0'
$newSdk = Join-Path $kits '10.0.26100.0'
foreach ($path in @(
    $oldLink, $newLink,
    (Join-Path $msvc '14.50.35717\lib\x64\msvcrt.lib'),
    (Join-Path $oldSdk 'um\x64\advapi32.lib'),
    (Join-Path $oldSdk 'ucrt\x64\ucrt.lib'),
    (Join-Path $newSdk 'um\x64\advapi32.lib'),
    (Join-Path $newSdk 'ucrt\x64\ucrt.lib')
)) {
    New-Item -ItemType File -Path $path -Force | Out-Null
}

$found = Resolve-RunTestsNativeToolchainPaths -MsvcToolsRoot $msvc -WindowsKitsLibRoot $kits
if (-not $found.Ok) { throw $found.Message }
Assert-Equal $found.Linker $newLink 'highest msvc linker'
Assert-Equal $found.LibPaths.Count 3 'lib path count'
if ($found.LibPaths[1] -notlike '*10.0.26100.0*ucrt*') { throw "ucrt path was $($found.LibPaths[1])" }
if ($found.LibPaths[2] -notlike '*10.0.26100.0*um*') { throw "um path was $($found.LibPaths[2])" }
foreach ($lib in $found.LibPaths) {
    if ($lib -match 'cannot find the file specified') { throw "diagnostic text leaked into $lib" }
}

$missingSdk = Resolve-RunTestsNativeToolchainPaths -MsvcToolsRoot $msvc -WindowsKitsLibRoot (Join-Path $root 'no-sdk')
if ($missingSdk.Ok) { throw 'missing Windows SDK was accepted' }
if ($missingSdk.Message -notmatch 'advapi32.lib') { throw $missingSdk.Message }
Assert-Equal $missingSdk.Linker $newLink 'linker still reported when sdk is missing'

$missingMsvc = Resolve-RunTestsNativeToolchainPaths -MsvcToolsRoot (Join-Path $root 'no-msvc') -WindowsKitsLibRoot $kits
if ($missingMsvc.Ok) { throw 'missing MSVC was accepted' }
if ($missingMsvc.Message -notmatch 'link.exe') { throw $missingMsvc.Message }

Write-Host 'toolchain_tests_ok'
