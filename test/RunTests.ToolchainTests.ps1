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

$instance = Get-RunTestsInstancePathFromLinker -Linker $newLink
if ((Split-Path -Leaf $instance) -ne 'BuildTools') { throw "instance path was $instance" }

$complete = [pscustomobject]@{ Ok = $true; Linker = $newLink }
if (@(Get-RunTestsAotComponentsToAdd $complete).Count -ne 0) { throw 'complete toolchain requested an install' }
$sdkOnly = [pscustomobject]@{ Ok = $false; Linker = $newLink }
$sdkComponents = @(Get-RunTestsAotComponentsToAdd $sdkOnly)
if ($sdkComponents.Count -ne 1 -or $sdkComponents[0] -ne 'Microsoft.VisualStudio.Component.Windows11SDK.26100') { throw "sdk components were $($sdkComponents -join ',')" }
$neither = [pscustomobject]@{ Ok = $false; Linker = $null }
$both = @(Get-RunTestsAotComponentsToAdd $neither)
if ($both -notcontains 'Microsoft.VisualStudio.Component.VC.Tools.x86.x64' -or $both -notcontains 'Microsoft.VisualStudio.Component.Windows11SDK.26100') { throw 'missing tools did not request both components' }

Assert-Equal (Get-RunTestsInstallerOutcome 0) 'Success' 'exit 0'
Assert-Equal (Get-RunTestsInstallerOutcome 3010) 'RebootRequired' 'exit 3010'
Assert-Equal (Get-RunTestsInstallerOutcome 1641) 'RebootStarted' 'exit 1641'
Assert-Equal (Get-RunTestsInstallerOutcome 1602) 'Cancelled' 'exit 1602'
Assert-Equal (Get-RunTestsInstallerOutcome 5004) 'Cancelled' 'exit 5004'
Assert-Equal (Get-RunTestsInstallerOutcome 1618) 'AnotherInstall' 'exit 1618'
Assert-Equal (Get-RunTestsInstallerOutcome 1) 'Failed' 'exit 1'
if (-not (Test-RunTestsMicrosoftSigner -Status 'Valid' -Subject 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond')) { throw 'valid Microsoft signature rejected' }
if (Test-RunTestsMicrosoftSigner -Status 'NotSigned' -Subject 'O=Microsoft Corporation') { throw 'unsigned payload accepted' }
Assert-Equal (Get-RunTestsBuildToolsBootstrapperUrl '') 'https://aka.ms/vs/18/stable/vs_buildtools.exe' 'default bootstrapper'
Assert-Equal (Get-RunTestsBuildToolsBootstrapperUrl 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools') 'https://aka.ms/vs/17/release/vs_buildtools.exe' 'vs2022 bootstrapper'

$script:calls = 0
$already = Install-RunTestsAotPrerequisites -Detect { $complete } -RunInstaller { param($c) $script:calls++; return 0 }
if (-not $already.Ok -or $already.Installed) { throw 'already-complete path installed something' }
if ($script:calls -ne 0) { throw 'installer ran when prerequisites were complete' }

$state = 'missing'
$reboot = Install-RunTestsAotPrerequisites -Detect { if ($state -eq 'ready') { $complete } else { $sdkOnly } } -RunInstaller { param($c) return 3010 }
if ($reboot.Ok -or -not $reboot.NeedsReboot) { throw 'reboot was treated as success' }
if ($reboot.Message -notmatch 'reboot') { throw $reboot.Message }

$cancel = Install-RunTestsAotPrerequisites -Detect { $sdkOnly } -RunInstaller { param($c) return 1602 }
if ($cancel.Ok -or $cancel.Message -notmatch 'canceled') { throw $cancel.Message }

$failed = Install-RunTestsAotPrerequisites -Detect { $sdkOnly } -RunInstaller { param($c) return 1 }
if ($failed.Ok -or $failed.Message -notmatch 'exit 1') { throw $failed.Message }

$partial = Install-RunTestsAotPrerequisites -Detect { $sdkOnly } -RunInstaller { param($c) return 0 }
if ($partial.Ok -or $partial.Message -notmatch 'still incomplete') { throw $partial.Message }

$installed = Install-RunTestsAotPrerequisites -Detect { if ($script:ready) { $complete } else { $sdkOnly } } -RunInstaller { param($c) $script:ready = $true; return 0 }
if (-not $installed.Ok -or -not $installed.Installed) { throw 'successful install was not accepted after re-detect' }

$installPath = 'C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools'
$commandLine = ConvertTo-RunTestsNativeCommandLine @(
    'modify', '--installPath', $installPath,
    '--add', 'Microsoft.VisualStudio.Component.Windows11SDK.26100',
    '--wait'
)
if ($commandLine -notmatch '--installPath "C:\\Program Files \(x86\)\\Microsoft Visual Studio\\18\\BuildTools"') {
    throw "install path was not quoted: $commandLine"
}
$python = Get-Command python3 -ErrorAction SilentlyContinue
if (-not $python) { throw 'python3 is required to check native argv' }
$argv = Join-Path ([System.IO.Path]::GetTempPath()) 'runtests-argv.py'
Set-Content -LiteralPath $argv -Value @'
import sys
print("COUNT", len(sys.argv) - 1)
for index, arg in enumerate(sys.argv[1:], 1):
    print("ARG%d=%s" % (index, arg))
'@ -Encoding ascii
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $python.Source
$start.Arguments = (ConvertTo-RunTestsNativeCommandLine @($argv, 'modify', '--installPath', $installPath, '--add', 'Microsoft.VisualStudio.Component.Windows11SDK.26100', '--wait'))
$start.UseShellExecute = $false
$start.RedirectStandardOutput = $true
$process = [System.Diagnostics.Process]::Start($start)
$native = $process.StandardOutput.ReadToEnd()
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw $native }
if ($native -notmatch 'ARG3=C:\\Program Files \(x86\)\\Microsoft Visual Studio\\18\\BuildTools') {
    throw "native argv split the install path: $native"
}
if ($native -notmatch 'COUNT 6') { throw "unexpected argv count: $native" }

Write-Host 'toolchain_tests_ok'
