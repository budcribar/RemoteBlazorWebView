# Release process

This describes how a maintainer publishes the five PeakSWC packages. The tutorial repository is not a NuGet package. It is updated on GitHub after the packages exist.

The workflow that packs the packages is [release-packages.yml](../.github/workflows/release-packages.yml). Its NuGet push job does not run.

## The five packages

Packed in this order, because WinForms, WPF, and the Photino host project-reference `RemoteWebView`:

1. `PeakSWC.RemoteWebView`
2. `PeakSWC.RemoteWebViewService`
3. `PeakSWC.RemoteBlazorWebView.WindowsForms`
4. `PeakSWC.RemoteBlazorWebView.Wpf`
5. `PeakSWC.RemoteBlazorWebView`

`RemoteBlazorWebView` is built before anyone builds the tutorial. The tutorial itself is never packed. The pack job checks it out only because the three UI packages include its `README.md` through a path next to this repo. That checkout is not modified and is not pushed.

## Version rule

`Directory.Build.props` is the only place a package version is committed. Today on net9 that file sets `VersionPrefix` to `9.0.21` and does not set `VersionSuffix`, so the package version is `9.0.21`. `global.json` pins SDK `9.0.0`, which is GA.

The rule on this line:

- The package major is the .NET major of the line (`net9` is 9, `net10` is 10, `net11` is 11). It must match the major of `sdk.version` in that line's `global.json` and the major of `VersionPrefix`.
- When that SDK is GA (no `preview`, `rc`, `alpha`, `beta`, or `pre`), the version is the committed `VersionPrefix` and `VersionSuffix`. On net9 that is `9.0.21` with no suffix. The script does not replace a GA SDK with `9.0.0`.
- When the SDK is a prerelease, `VersionPrefix` is `{major}.0.0` unless `version_override` is set. The prerelease label comes from that SDK token, written the way packages on nuget.org already are: the dot inside the token is removed. `rc.2` becomes `rc2`. `preview.5` becomes `preview5`. A bare `pre` stays `pre`. A committed suffix spelled `rc.2` or `preview.5` is rewritten the same way.
- `version_override` is a single token such as `9.0.22` or `9.0.21-rc2`. The text before the first hyphen is `VersionPrefix`. The text after it is `VersionSuffix`. It replaces both the committed GA version and the prerelease derivation.
- `patch` is empty or `0` for none. A positive integer `N` appends `-pN`. `9.0.21` with patch `1` is `9.0.21-p1` (`VersionSuffix` `p1`).

`9.0.21` is already on nuget.org. The script reads each package's public flat-container index (`https://api.nuget.org/v3-flatcontainer/<id>/index.json`, no account and no key) and prints a warning when the derived version, including a `-pN` build, is already listed. A warning does not stop the script. The next package has to be a new version, such as `9.0.22`.

SemVer treats a hyphen as the start of a prerelease. `9.0.21-p1` sorts **before** `9.0.21`, because every prerelease is less than the release with the same numbers. For a stable package that must install over `9.0.21`, set `version_override` to `9.0.22` and leave `patch` empty. An rc patch such as `11.0.0-rc2-p1` sorts after `11.0.0-rc2` because `rc2-p1` is greater than `rc2` in ASCII order. Use `-pN` for another build of an rc, not for the next stable net9 package.

`eng/derive-package-version.sh` implements this and does not edit the tree. The workflow prints the result and uploads it. It does not commit and it does not push. The maintainer commits the two property values in `Directory.Build.props` on the line being released.

## What a dispatch does

Dispatch **Release packages** on net9. The copy of this workflow on net11 still turns a GA SDK into `{major}.0.0`, so pack net9 from this branch. Inputs:

| Input | Default | Meaning |
|---|---|---|
| `line` | required | `net9`, `net10`, or `net11`. The workflow checks out that branch for the pack. |
| `version_override` | empty | Replaces the derived version. For example `9.0.22`. |
| `patch` | empty | Servicing counter. Empty or `0` adds nothing. `1` appends `-p1`. |
| `dry_run` | `true` | Packs and uploads artifacts. net9 still runs Windows tests when `line` is net9. Does not push to NuGet. |

Jobs:

1. **Version and notes.** Derives the version and writes release notes: a `Based on maui <version>` line, then commit subjects since the last tag. Subjects that mention `LoadTest`, `ceiling`, `req/s`, `requests per`, or `throughput` are omitted. There is no performance claim in the generated notes. Notes stay within 4000 characters; a longer list is cut and linked to the tag compare view. The job uploads `PackageReleaseNotes.txt` and `release-notes.patch`. It also leaves `eng/apply-release-notes.sh` for the same edit. The workflow does not commit or push that edit. The maintainer applies the patch, or runs the script, and commits so the five `PackageReleaseNotes` properties match the published text.
2. **Windows tests.** Runs when `line` is `net9`, whether or not `dry_run` is set. It calls this branch's `.github/workflows/windows-tests.yml` (`workflow_call`). Packing and the artifact upload wait for that job to pass. A dispatch with another line prints a warning and does not call this suite.
3. **Pack.** Builds `remote.blazor.desktop.js`, then packs the five projects. When `line` is net9 this starts only after Windows tests pass. The project paths, package ids, and the tutorial `README.md` path are the same as on net11. Each package's `PackageReleaseNotes` for that pack is the generated notes, so the artifact matches the text the maintainer is asked to commit. The job uploads the five `.nupkg` files and `SHA256SUMS.txt`.
4. **Push to NuGet.org.** The job condition compares `dry_run` (a boolean) to the sentinel `__nuget_push_disabled__`, which the dispatch form cannot set. GitHub does not schedule the job. A literal `if: false` was not used because actionlint rejects that constant.

After the run, download `PackageReleaseNotes.txt` and `release-notes.patch` from the `release-plan-<line>` artifact. On the line being released, either `git apply release-notes.patch` or `eng/apply-release-notes.sh . PackageReleaseNotes.txt`. Review the five `PackageReleaseNotes` edits, commit them with the `Directory.Build.props` version, and push that commit yourself. The workflow never does that commit.

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
