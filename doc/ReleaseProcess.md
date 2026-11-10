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

`Directory.Build.props` is the only place a package version is committed. Today on net11 that file sets `VersionPrefix` to `11.0.0` and `VersionSuffix` to `rc3`, so the unpublished package version is `11.0.0-rc3`. That suffix matches the ASP.NET Core 11.0.0-rc.3 libraries this tree references and the SDK pin `11.0.100-rc.3.26522.148` in `global.json`. `eng/derive-package-version.sh` derives `rc3` from that SDK unless `version_override` is set. This file does not publish either version.

The rule going forward:

- The package major is the .NET major of the line (`net9` is 9, `net10` is 10, `net11` is 11). It must match the major of `sdk.version` in that line's `global.json`.
- `VersionPrefix` is `{major}.0.0` unless the dispatch input `version_override` says otherwise.
- The prerelease label comes from that SDK token, written the way packages on nuget.org already are: the dot inside the token is removed. `11.0.100-rc.2.26504.105` becomes `VersionSuffix` `rc2` and package version `11.0.0-rc2`. `preview.5` becomes `preview5`. A bare `pre` stays `pre`. A GA SDK (no `preview`, `rc`, `alpha`, `beta`, or `pre`) leaves `VersionSuffix` empty.
- `version_override` is a single token such as `11.0.1` or `11.0.0-rc3`. The text before the first hyphen is `VersionPrefix`. The text after it is `VersionSuffix`.
- `patch` is empty or `0` for none. A positive integer `N` appends `-pN`. `11.0.0-rc2` with patch `1` is `11.0.0-rc2-p1` (`VersionSuffix` `rc2-p1`). A GA `11.0.0` with patch `1` is `11.0.0-p1` (`VersionSuffix` `p1`).

`11.0.0-rc2` is already on nuget.org for `PeakSWC.RemoteWebView` and `PeakSWC.RemoteBlazorWebView`. The next net11 package has to be a new version, such as `11.0.0-rc3` or `11.0.0`. The script reads each package's public flat-container index (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`, no account and no key) and prints a warning when the derived version, including a `-pN` build, is already listed. A warning does not stop the script.

SemVer treats a hyphen as the start of a prerelease. `11.0.0-rc2-p1` is one prerelease identifier (`rc2-p1`). It sorts after `11.0.0-rc2` because `rc2-p1` is greater than `rc2` in ASCII order, so a patch on an rc is a later prerelease. `11.0.0-p1` sorts **before** `11.0.0`, because every prerelease is less than the release with the same numbers. Use `-pN` for another build of an rc. For a stable package that must install over `11.0.0`, set `version_override` to `11.0.1` and leave `patch` empty.

`eng/derive-package-version.sh` implements this and does not edit the tree. The workflow prints the result and uploads it. It does not commit and it does not push. The maintainer commits the two property values in `Directory.Build.props` on the line being released.

## Upstream MAUI and Web.JS sources (`EditWebView`)

`src/EditWebView` refreshes the vendored upstream files from `dotnet/aspnetcore` and `dotnet/maui`:

- Copies `src/Components/Web.JS` from the `dotnet/aspnetcore` tag archive into `src/RemoteWebView.Blazor.JS/Web.JS` (compiled into `dist/remote.blazor.desktop.js`), preserving repo-local `Web.JS/src/{tsconfig.json,webpack.config.js}` overlays and pointing `@microsoft/dotnet-runtime` at `file:../dotnet-runtime`.
- Transforms `src/BlazorWebView/src/{WindowsForms,Wpf,SharedSource}` from the `dotnet/maui` tag archive into `src/RemoteBlazorWebView.WinForms`, `src/RemoteBlazorWebView.Wpf`, and `src/SharedSource` (`PeakSWC.*` namespaces, `AllowExternalDrop = false` on WinForms `WebView2` and WPF `IWebView2`, `NavigateToString`, mirror URL routing, and public visibility).

When to run it:

- Run `EditWebView` when moving to a new major, preview, or RC, or when a servicing release changes `src/Components/Web.JS` in `dotnet/aspnetcore` or publishes a new `dotnet/maui` tag on this line.
- Skip it for runtime-only servicing releases where neither upstream directory changed.

How to run it on `net11`:

1. To preview the latest upstream MAUI tag without modifying files, run `dotnet run --project src/EditWebView -- --resolve-only --major 11` (uses `git ls-remote --tags`; no `GITHUB_PAT` required).
2. Update `AspNetCoreTagHint` and `MauiTagHint` in `src/EditWebView/Program.cs` (or override them via `ASPNETCORE_TAG` and `MAUI_TAG`; setting either environment variable to an empty string forces a live tag scan). `TARGET_MAJOR_VERSION` or `--major <N>` overrides the major version.
3. Build `src/EditWebView/EditWebView.csproj` with `-p:Platform=x64` (or in Visual Studio with `Debug | x64`), then run the executable from `src/EditWebView/bin/x64/Debug/net11.0` (Visual Studio's default working directory). Both `RepoPath` (six levels up from `BaseDirectory`) and `RelativePath` (five levels up from the working directory to `src/`) assume the `bin/x64/<Configuration>/<tfm>` layout.
4. Inspect `git diff` carefully before committing: `EditWebView` overwrites `src/SharedSource`, `src/RemoteBlazorWebView.Wpf`, `src/RemoteBlazorWebView.WinForms`, and `src/RemoteWebView.Blazor.JS/Web.JS` directly from upstream, so re-apply any repo-local changes made on top of the vendored files (for example shared-control, voice, or `NavigateToUrl` additions).
5. Rebuild `src/RemoteWebView.Blazor.JS/dist/remote.blazor.desktop.js` (`npm run build:production`) and verify the solution builds and tests pass before committing.

## What a dispatch does

Dispatch **Release packages** on `cursor/release-workflow-205f` (and, once reviewed, on the line you are releasing). Inputs:

| Input | Default | Meaning |
|---|---|---|
| `line` | required | `net9`, `net10`, or `net11`. The workflow checks out that branch for the pack. |
| `version_override` | empty | Replaces the derived version. Use the no-dot spelling, for example `11.0.0-rc3`. |
| `patch` | empty | Servicing counter. Empty or `0` adds nothing. `1` appends `-p1`. |
| `dry_run` | `true` | Packs and uploads artifacts. net11 still runs Windows tests. Does not push to NuGet. |

Jobs:

1. **Version and notes.** Derives the version and writes release notes: a `Based on maui <version>` line, then commit subjects since the last tag. Subjects that mention `LoadTest`, `ceiling`, `req/s`, `requests per`, or `throughput` are omitted. There is no performance claim in the generated notes. Notes stay within 4000 characters; a longer list is cut and linked to the tag compare view. The job uploads `PackageReleaseNotes.txt` and `release-notes.patch`. It also leaves `eng/apply-release-notes.sh` for the same edit. The workflow does not commit or push that edit. The maintainer applies the patch, or runs the script, and commits so the seven `PackageReleaseNotes` properties match the published text.
2. **Windows tests.** Runs for `net11` whether or not `dry_run` is set. It calls `.github/workflows/windows-tests.yml` (`workflow_call` on this branch). Packing and the artifact upload wait for that job to pass. net9 and net10 still use their own Windows tests workflows, which are not reusable yet. A pack of those lines prints a warning and does not call the net11 suite.
3. **Pack.** Builds `remote.blazor.desktop.js`, then packs the seven projects. For net11 this starts only after Windows tests pass. Each package's `PackageReleaseNotes` for that pack is the generated notes, so the artifact matches the text the maintainer is asked to commit. The job uploads the seven `.nupkg` files and `SHA256SUMS.txt`.
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
