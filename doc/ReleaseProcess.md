# Release process

This describes how a maintainer publishes the seven PeakSWC packages. The tutorial repository is not a NuGet package. It is updated on GitHub after the packages exist.

The workflow that packs the packages is [release-packages.yml](../.github/workflows/release-packages.yml). Its NuGet push job does not run.

## The seven packages

Packed in this order, because `RemoteWebViewService` project-references `RemoteWebViewService.Lib`, and WinForms, WPF (which also project-references `RemoteBlazorWebView.RemoteComponents`), and the Photino host project-reference `RemoteWebView`:

1. `PeakSWC.RemoteWebView`
2. `PeakSWC.RemoteWebViewService.Lib`
3. `PeakSWC.RemoteWebViewService`
4. `PeakSWC.RemoteBlazorWebView.RemoteComponents`
5. `PeakSWC.RemoteBlazorWebView.WindowsForms`
6. `PeakSWC.RemoteBlazorWebView.Wpf`
7. `PeakSWC.RemoteBlazorWebView`

`RemoteBlazorWebView` is built before anyone builds the tutorial. The tutorial itself is never packed. The pack job checks it out only because the three UI packages include its `README.md` through a path next to this repo. That checkout is not modified and is not pushed.

## Version rule

`Directory.Build.props` is the only place a package version is committed. Today on net10 that file sets `VersionPrefix` to `10.0.15` and does not set `VersionSuffix`, so the package version is `10.0.15`. `global.json` pins SDK `10.0.404`, which is GA.

The rule on this line:

- The package major is the .NET major of the line (`net9` is 9, `net10` is 10, `net11` is 11). It must match the major of `sdk.version` in that line's `global.json` and the major of `VersionPrefix`.
- When that SDK is GA (no `preview`, `rc`, `alpha`, `beta`, or `pre`), the version is the committed `VersionPrefix` and `VersionSuffix`. On net10 that is `10.0.15` with no suffix. The script does not replace a GA SDK with `10.0.0`.
- When the SDK is a prerelease, `VersionPrefix` is `{major}.0.0` unless `version_override` is set. The prerelease label comes from that SDK token, written the way packages on nuget.org already are: the dot inside the token is removed. `rc.2` becomes `rc2`. `preview.5` becomes `preview5`. A bare `pre` stays `pre`. A committed suffix spelled `rc.2` or `preview.5` is rewritten the same way.
- `version_override` is a single token such as `10.0.14` or `10.0.13-rc2`. The text before the first hyphen is `VersionPrefix`. The text after it is `VersionSuffix`. It replaces both the committed GA version and the prerelease derivation.
- `patch` is empty or `0` for none. A positive integer `N` appends `-pN`. `10.0.13` with patch `1` is `10.0.13-p1` (`VersionSuffix` `p1`).

`10.0.13` is already on nuget.org. `10.0.15` is the committed version and is not published by this file. The script reads each package's public flat-container index (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`, no account and no key) and prints a warning when the derived version, including a `-pN` build, is already listed. A warning does not stop the script. The next package after one that is already listed has to be a new version.

SemVer treats a hyphen as the start of a prerelease. `10.0.13-p1` sorts **before** `10.0.13`, because every prerelease is less than the release with the same numbers. For a stable package that must install over `10.0.13`, set `version_override` to `10.0.14` and leave `patch` empty. An rc patch such as `11.0.0-rc2-p1` sorts after `11.0.0-rc2` because `rc2-p1` is greater than `rc2` in ASCII order. Use `-pN` for another build of an rc, not for the next stable net10 package.

`eng/derive-package-version.sh` implements this and does not edit the tree. The workflow prints the result and uploads it. It does not commit and it does not push. The maintainer commits the two property values in `Directory.Build.props` on the line being released.

## Upstream MAUI and Web.JS sources (`EditWebView`)

`src/EditWebView` refreshes the vendored upstream files from `dotnet/aspnetcore` and `dotnet/maui`:

- Copies `src/Components/Web.JS` from the `dotnet/aspnetcore` tag archive into `src/RemoteWebView.Blazor.JS/Web.JS` (compiled into `dist/remote.blazor.desktop.js`).
- Transforms `src/BlazorWebView/src/{WindowsForms,Wpf,SharedSource}` from the `dotnet/maui` tag archive into `src/RemoteBlazorWebView.WinForms`, `src/RemoteBlazorWebView.Wpf`, and `src/SharedSource` (`PeakSWC.*` namespaces, `AllowExternalDrop = false`, `NavigateToString`, mirror URL routing, and public visibility).

When to run it:

- Run `EditWebView` when moving to a new major, preview, or RC, or when a servicing release changes `src/Components/Web.JS` in `dotnet/aspnetcore` or publishes a new `dotnet/maui` tag on this line.
- Skip it for runtime-only servicing releases where neither upstream directory changed.

How to run it on `net10`:

1. Give it a GitHub token for reading tags: either sign in once with `gh auth login` (EditWebView runs `gh auth token`), or set `GITHUB_PAT`, which wins when both are present.
2. Build `src/EditWebView/EditWebView.csproj` with `-p:Platform=x64` (or in Visual Studio with `Debug | x64`), then run the executable from `src/EditWebView/bin/x64/Debug/net10.0` (Visual Studio's default working directory). Both `RepoPath` (six levels up from `BaseDirectory`) and `RelativePath` (five levels up from the working directory to `src/`) assume the `bin/x64/<Configuration>/<tfm>` layout.
3. Read the summary it prints at the end, and `EditWebView.diff` in the repository root for the whole diff (git-ignored):
   - **Upstream versions used.** A warning here means the tag is newer than the packages reference: update `Microsoft.AspNetCore.Components.WebView.Wpf` and `.WindowsForms` and the "Based on maui ..." text in the package release notes.
   - **Changes of this repository's own.** `EditWebView` overwrites `src/SharedSource`, `src/RemoteBlazorWebView.Wpf`, `src/RemoteBlazorWebView.WinForms` and `src/RemoteWebView.Blazor.JS/Web.JS` from upstream and then makes this repository's changes again (`Editor.cs` for the C# files, `ApplyWebJSPatches` and `OurWebJSFiles` in `Program.cs` for Web.JS). Any line that says NOT APPLIED, NOT KEPT, NOT REMOVED or MISSING means upstream changed underneath one of them: fix that before committing.
   - **Changes since the last commit.** Everything listed should be an upstream change. A removed line that this repository added is a change `EditWebView` does not know about yet: put it back, and add it to `Editor.cs` so the next run keeps it.
   - Upstream's `build/*.props` and `build/*.targets` and its unit-test `InternalsVisibleTo` are left out on purpose.
4. Rebuild `src/RemoteWebView.Blazor.JS/dist/remote.blazor.desktop.js` (`npm run build:production`) and verify the solution builds and tests pass before committing.

## What a dispatch does

Dispatch **Release packages** on net10. The copy of this workflow on net11 still turns a GA SDK into `{major}.0.0`, so pack net10 from this branch. Inputs:

| Input | Default | Meaning |
|---|---|---|
| `line` | required | `net9`, `net10`, or `net11`. The workflow checks out that branch for the pack. |
| `version_override` | empty | Replaces the derived version. For example `10.0.14`. |
| `patch` | empty | Servicing counter. Empty or `0` adds nothing. `1` appends `-p1`. |
| `dry_run` | `true` | Packs and uploads artifacts. net10 still runs Windows tests when `line` is net10. Does not push to NuGet. |

Jobs:

1. **Version and notes.** Derives the version and writes release notes: a `Based on maui <version>` line, then commit subjects since the last tag. Subjects that mention `LoadTest`, `ceiling`, `req/s`, `requests per`, or `throughput` are omitted. There is no performance claim in the generated notes. Notes stay within 4000 characters; a longer list is cut and linked to the tag compare view. The job uploads `PackageReleaseNotes.txt` and `release-notes.patch`. It also leaves `eng/apply-release-notes.sh` for the same edit. The workflow does not commit or push that edit. The maintainer applies the patch, or runs the script, and commits so the seven `PackageReleaseNotes` properties match the published text.
2. **Windows tests.** Runs when `line` is `net10`, whether or not `dry_run` is set. It calls this branch's `.github/workflows/windows-tests.yml` (`workflow_call`). Packing and the artifact upload wait for that job to pass. A dispatch with another line prints a warning and does not call this suite.
3. **Pack.** Builds `remote.blazor.desktop.js`, then packs the seven projects. When `line` is net10 this starts only after Windows tests pass. Each package's `PackageReleaseNotes` for that pack is the generated notes, so the artifact matches the text the maintainer is asked to commit. The job uploads the seven `.nupkg` files and `SHA256SUMS.txt`.
4. **Push to NuGet.org.** The job condition compares `dry_run` (a boolean) to the sentinel `__nuget_push_disabled__`, which the dispatch form cannot set. GitHub does not schedule the job. A literal `if: false` was not used because actionlint rejects that constant.

After the run, download `PackageReleaseNotes.txt` and `release-notes.patch` from the `release-plan-<line>` artifact. On the line being released, either `git apply release-notes.patch` or `eng/apply-release-notes.sh . PackageReleaseNotes.txt`. Review the seven `PackageReleaseNotes` edits, commit them with the `Directory.Build.props` version, and push that commit yourself. The workflow never does that commit.

## Daily .NET pulse

BlazorBot runs a separate read-only check of the .NET SDK and runtime for each line. It does not pack, tag, or push. When that check reports a new phase (for example rc2 becoming rc3, or an rc becoming GA), the maintainer decides whether to dispatch this workflow. The pulse does not start the release, and the release workflow does not call the pulse.

## Trusted publishing setup (one time)

The nuget.org profile name is `budcribar`. The workflow reads it from the repository variable `NUGET_USER`. That variable is a name, not a secret, and it is not an API key. Do not add `NUGET_API_KEY`.

Confirm the policy fields against [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) before creating it. Checked against that page: the policy is repository owner, repository name, workflow **file name only**, and an optional Actions environment. Once the push job is allowed to run, `NuGet/login@v1` exchanges a GitHub OIDC token for one short-lived API key (about one hour, one exchange per token). The username is the nuget.org profile name, not an email address. No long-lived API key is stored.

Create these by hand. This repository's workflow does not create them, and the push job stays unreachable until someone later changes its condition on purpose.

1. In GitHub, create the `nuget-org` environment and set **budcribar** as a required reviewer.
2. Add the repository variable `NUGET_USER` with the value `budcribar`.
3. On nuget.org, open the account menu, choose **Trusted Publishing**, and add a policy:
   - Repository owner: `budcribar`
   - Repository: `RemoteBlazorWebView`
   - Workflow file: `release-packages.yml` (the file name only, not `.github/workflows/release-packages.yml`)
   - Environment: `nuget-org`

Only after those exist, replace the `nuget-push` job condition and review the workflow again. Until that edit, the job cannot run. Setting `dry_run` to false does not enable it either.

The push source, once the job is allowed to run, is `https://api.nuget.org/v3/index.json`. The key used there is `steps.login.outputs.NUGET_API_KEY`, the temporary key from the login action, not a repository secret.
