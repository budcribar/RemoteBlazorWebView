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

`Directory.Build.props` is the only place a package version is committed. Today on net11 that file sets `VersionPrefix` to `11.0.0` and `VersionSuffix` to `rc2`, so the package version is `11.0.0-rc2`.

The rule going forward:

- The package major is the .NET major of the line (`net9` is 9, `net10` is 10, `net11` is 11). It must match the major of `sdk.version` in that line's `global.json`.
- `VersionPrefix` is `{major}.0.0` unless the dispatch input `version_override` says otherwise.
- The prerelease label is taken from that SDK version, using the SDK's own spelling. `11.0.100-rc.2.26504.105` becomes `VersionSuffix` `rc.2`. A preview SDK becomes `preview.N`. A GA SDK (no `-preview`, `-rc`, `-alpha`, or `-beta`) leaves `VersionSuffix` empty.
- `version_override` is a single token such as `11.0.1` or `11.0.0-rc.3`. The text before the first hyphen is `VersionPrefix`. The text after it is `VersionSuffix`.

`eng/derive-package-version.sh` implements this and does not edit the tree. The workflow prints the result and uploads it. It does not commit and it does not push. The maintainer commits the two property values in `Directory.Build.props` on the line being released.

The current suffix spelling `rc2` and the rule's spelling `rc.2` are different version identities. See the open question below before the first published package on a line uses the new spelling.

## What a dispatch does

Dispatch **Release packages** on `cursor/release-workflow-205f` (and, once reviewed, on the line you are releasing). Inputs:

| Input | Default | Meaning |
|---|---|---|
| `line` | required | `net9`, `net10`, or `net11`. The workflow checks out that branch for the pack. |
| `version_override` | empty | Replaces the derived version. |
| `dry_run` | `true` | Packs and uploads artifacts. Skips the Windows test call. Does not push to NuGet. |

Jobs:

1. **Version and notes.** Derives the version and writes release notes from commits since the last tag. Subjects that mention `LoadTest`, `ceiling`, `req/s`, `requests per`, or `throughput` are omitted. There is no performance claim in the generated notes.
2. **Windows tests.** Runs only when `line` is `net11` and `dry_run` is false. It calls `.github/workflows/windows-tests.yml` (`workflow_call` on this branch). net9 and net10 still use their own Windows tests workflows, which are not reusable yet. A non-dry-run pack of those lines prints a warning and does not call the net11 suite.
3. **Pack.** Builds `remote.blazor.desktop.js`, then packs the five projects. Each package's `PackageReleaseNotes` for that pack is the generated notes. The committed csproj notes are not edited. The job uploads the five `.nupkg` files and `SHA256SUMS.txt`.
4. **Push to NuGet.org.** The job condition compares `dry_run` (a boolean) to the sentinel `__nuget_push_disabled__`, which the dispatch form cannot set. GitHub does not schedule the job. A literal `if: false` was not used because actionlint rejects that constant.

## Daily .NET pulse

BlazorBot runs a separate read-only check of the .NET SDK and runtime for each line. It does not pack, tag, or push. When that check reports a new phase (for example rc.2 becoming rc.3, or an rc becoming GA), the maintainer decides whether to dispatch this workflow. The pulse does not start the release, and the release workflow does not call the pulse.

## Trusted publishing setup (one time)

Confirm these fields against [Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) before creating the policy. Checked against that page: the policy is repository owner, repository name, workflow **file name only**, and an optional Actions environment. The workflow exchanges a GitHub OIDC token through `NuGet/login@v1` for one short-lived API key (about one hour, one exchange per token). The username is the nuget.org profile name, not an email address. No long-lived API key is stored.

1. On nuget.org, open the account menu and choose **Trusted Publishing**.
2. Add a policy for this repository:
   - Repository owner: `budcribar`
   - Repository: `RemoteBlazorWebView`
   - Workflow file: `release-packages.yml` (the file name only, not `.github/workflows/release-packages.yml`)
   - Environment: `nuget-org`
3. In GitHub, create an environment named `nuget-org` and require a reviewer. That is the approval gate.
4. Add a repository variable `NUGET_USER` whose value is the nuget.org profile name. This is not an API key. Do not add `NUGET_API_KEY`.
5. Only after those exist, replace the `nuget-push` job condition and review the workflow again. Until that edit, the job cannot run. Setting `dry_run` to false does not enable it either.

The push source, once the job is allowed to run, is `https://api.nuget.org/v3/index.json`. The key used there is `steps.login.outputs.NUGET_API_KEY`, the temporary key from the login action, not a repository secret.
