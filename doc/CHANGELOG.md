# Changelog

## 9.0.22

Servicing release prepared on November 6, 2026. The packages are not published.

- Package version `9.0.22` from `VersionPrefix` in `Directory.Build.props`. No `VersionSuffix`.
- SDK pin `9.0.320` in `global.json`, with `rollForward` left at `latestMinor`.
- Microsoft .NET 9 servicing packages that were directly referenced at `9.0.21` are `9.0.22`. Checked on the public NuGet flat-container index and against the [.NET 9.0.22 release notes](https://github.com/dotnet/core/blob/main/release-notes/9.0/9.0.22/9.0.22.md). `Microsoft.AspNetCore.Components` 9.0.22 and `System.Management` 9.0.22 were last modified October 25, 2026.
- `Microsoft.AspNetCore.Components.WebView.WindowsForms` stays `9.0.120`. That is the highest 9.0 version on NuGet (package last modified October 16, 2025) and it depends on `Microsoft.AspNetCore.Components.WebView` 9.0.9. Restore keeps that 9.0.9 package, so `RemoteWebView` and the WinForms package still get the 9.0.9 component graph from it (`Microsoft.AspNetCore.Components` 9.0.9 and the related `Microsoft.Extensions.*` 9.0.9 packages). `RemoteBlazorWebView` also restores `Microsoft.AspNetCore.Components.WebView` 9.0.9, while its own direct references lift `Microsoft.AspNetCore.Components` and `Microsoft.AspNetCore.Components.Web` to 9.0.22. The same WindowsForms package id has a `9.0.22` build last modified January 7, 2025, which depends on `Microsoft.AspNetCore.Components.WebView` 9.0.0. That older build is not used.
- Target frameworks stay `net9.0`, `net9.0-windows`, and `net9`.
- The [9.0 release index](https://github.com/dotnet/core/blob/main/release-notes/9.0/README.md) and `https://builds.dotnet.microsoft.com/dotnet/release-metadata/9.0/releases.json` list `latest-release` `9.0.22`, `latest-sdk` `9.0.320` (also SDK `9.0.123`), `latest-release-date` `2026-10-25`, `support-phase` `maintenance`, and `eol-date` `2026-11-10`. The release is marked security. Official notes describe CVE-2026-111497 (Brotli decoder out-of-bounds write), CVE-2026-114148 (Linux .NET tarball ownership), and a re-release of the CVE-2026-70197 fix because the October 13, 2026 release did not fully address it. This repository update tracks those Microsoft dependencies. It does not add a separate fix for those CVEs.

`9.0.21` of the five PeakSWC packages is already on nuget.org. `9.0.22` is not, as of November 6, 2026.

Behavior left as it is: `BrowserIPCService.SendMessage` creates a registry entry only when `Sequence` is 1 and `IsPrimary` is true, and otherwise looks up the entry or drops the message when it is absent. `WaitForClientShutdown` still identifies the registration with `ReferenceEquals`. SYSLIB1044 is still not fixed. The Windows test timer CA2027 is still not fixed.
