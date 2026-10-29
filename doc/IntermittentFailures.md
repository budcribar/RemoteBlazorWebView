# Intermittent CI failures — net10

Generated 2026-10-29 (America/Denver) from `gh run list`/`gh run view --log` for runs 2026-10-15 → now. Logs: /workspace/vlogs/run-<id>.log.

## Open intermittent failures (no fix)

None found.

## Fixed failures

| Date (UTC / America/Denver) | Run | SHA | Workflow | Failing test / step | Error excerpt | Class | Evidence of later pass |
|---|---|---|---|---|---|---|---|
| 2026-10-28 23:53 UTC / 2026-10-28 17:53 MDT | [40790404954](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40790404954) | 897c506 | Windows tests | Tutorial restore + Local TestClicks / TestRemoteBlazorDebugWpf / TestServerWpf | error NU1301; FileNotFoundException: Application executable not found; exited -532462766 before DevTools endpoint ready | FIXED (build/config) | 588a1ae run [40796806386](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40796806386) passed |
| 2026-10-29 00:15 UTC / 2026-10-28 18:15 MDT | [40792856175](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40792856175) | 54d190c | Windows tests | TestRemoteBlazorWinFormsDebug.Test1/2/5Client, Test2Client5Refresh; TestLocalBlazorWebViewDebug.TestClicks | Failed!  - Failed: 5, Passed: 64 (debug app path) | FIXED | 588a1ae "Keep BlazorWpfPath working directory in StartRemoteBlazorWpfDebugApp on net10" run [40796806386](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40796806386) |
| 2026-10-29 03:42 UTC / 2026-10-28 21:42 MDT | [40813641256](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40813641256) | 8a1b7c3 | Windows tests (reported success) | 8 StaticContentCache.EndToEnd ModuleInitializer*/BaselineModuleRun tests (hidden) | Failed!  - Failed: 8, Passed: 49, Total: 57 - StaticContentCache.EndToEnd.Tests.dll, but the job was green | FIXED | f1c27ff run [40867621208](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40867621208) 57/57; 5d0d6e4 run [40875155426](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40875155426) 58/58 |

### Playbook: hidden Chrome E2E failures (8a1b7c3)
1. **Read logs, not conclusions.** The run was green, but grepping the log for `Failed!` found 8 failed E2E tests.
2. **Masking:** fixed by f1c27ff "Fail the module-serving step when either test suite fails." The commit message says: "PowerShell was keeping only the lifecycle exit code, so end-to-end failures were hidden when that suite passed." Only .github/workflows/windows-tests.yml changed. (The same subject appears on net9 as 6b83971.)
3. **Root cause:** fixed by 5d0d6e4 "Read Chrome output on a dedicated thread before waiting for DevTools." The commit message says: "A thread-pool reader can start after Chrome fills the redirected pipe, so the process stays up and the debug port never accepts connections. A failed start now kills that process." Files: ChromeDevToolsSession.cs plus the E2E test classes. Same subject on net9 (1575097) and net11 (84aad20).
4. **Verification:** each branch's next Windows run passed every E2E test (net10 40875155426, net9 40875333066, net11 40876544204).
- Reusable lesson: first make the CI step fail honestly, then fix the root cause. For a child process with redirected output, start the reader thread before waiting on the process.

### Playbook: debug-app path (897c506, 54d190c)
- Log evidence: `Application executable not found at path …` and the app exiting with -532462766. The tests failed in about 4 s, so the app never started. Fixed by 588a1ae in test/FileSyncServer.Tests/Utilities.cs. This was deterministic, not a flake.

## Pattern summary

- No open intermittent failures found on net10 in the window.
- 8a1b7c3: the job was green only because its exit code was masked. Treat green E2E runs before f1c27ff as unverified.

LoadTest/performance ceiling numbers are noisy measurements; not treated as failures or analyzed here.
