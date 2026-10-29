# Intermittent CI failures — net9

Generated 2026-10-29 (America/Denver) from `gh run list`/`gh run view --log` for runs 2026-10-15 → now. Logs: /workspace/vlogs/run-<id>.log.

## Open intermittent failures (no fix)

| Date (UTC / America/Denver) | Run | SHA | Workflow | Failing test / step | Error excerpt | Class | Evidence of later pass |
|---|---|---|---|---|---|---|---|
| 2026-10-27 17:43 UTC / 2026-10-27 11:43 MDT | [40553337082](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40553337082) | bb91014 | Windows tests | ConcurrentRequestsTests.MultipleConcurrentFileRequestsWithSameFile_BrowserAndClientCaching; TestServerWpf.Test1/2/5/10Client | TestServerWpf.*: at BaseTestRemoteFixture.VerifyServerStats(...) | INTERMITTENT | Same SHA passed in run [40553378845](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40553378845) |
| 2026-10-29 01:52 UTC / 2026-10-28 19:52 MDT | [40803005685](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40803005685) | cd0a0eb | Windows tests | TestServerForm.Test5Client | Assert.Equal() Failure: Values differ Expected: 125 Actual: 114 (VerifyServerStats) | INTERMITTENT | Same SHA passed in run [40808327075](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40808327075) |
| 2026-10-29 11:43 UTC / 2026-10-29 05:43 MDT | [40867619246](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40867619246) | 6b83971 | Windows tests | TestRemoteEmbeddedBlazorWebView.Test5Client | System.TimeoutException : Timeout 30000ms exceeded. navigating to "https://localhost:5001/app/…", waiting until "load" | INTERMITTENT (probable) / UNKNOWN | Passed on next commit 1575097 run [40875333066](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40875333066); 1575097 changes only E2E Chrome launcher, not this Playwright test, but not proven unrelated. Rerun of 6b83971 (40874896591) was cancelled. |

## Fixed failures

| Date (UTC / America/Denver) | Run | SHA | Workflow | Failing test / step | Error excerpt | Class | Evidence of later pass |
|---|---|---|---|---|---|---|---|
| 2026-10-28 23:54 UTC / 2026-10-28 17:54 MDT | [40790551384](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40790551384) | f8d2bd3 | Windows tests | Tutorial restore step → TestServerWpf/TestRemoteBlazorWpf.*Client | error NU1301: The local source '…' (tutorial restore failed) | FIXED (build/config, not flaky) | 53b40a7 run [40793531864](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40793531864) passed |
| 2026-10-29 00:15 UTC / 2026-10-28 18:15 MDT | [40792861760](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40792861760) | abdba54 | Windows tests | Tutorial restore step → TestServerWpf/TestRemoteBlazorWpf.*Client | error NU1605: Warning As Error: Detected package downgrade (RemoteBlazorWebViewTutorial.WinFormsApp) | FIXED (build/config) | 53b40a7 run [40793531864](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40793531864) passed |

### Playbook: tutorial package restore break (f8d2bd3, abdba54)
- Fix: 53b40a7 "Re-run net9 CI with synced RemoteBlazorWebViewTutorial 9.0.21 packages" (commit touched no files in this repo per the API, so the fix was presumably in the tutorial repo/packages; not verified).
- Evidence: logs show NU1301/NU1605 restore errors before tests; the WPF tests then failed after ~4 s each (the app was never built). That made it a deterministic setup failure, not a flake.
- Approach: when many related tests fail fast together, read the first `##[error]` in the log before reading test asserts.

## Pattern summary

- VerifyServerStats count mismatches: TestServerWpf (bb91014) and TestServerForm (cd0a0eb), both passed when rerun. Cause not investigated.
- ConcurrentRequestsTests.MultipleConcurrentFileRequestsWithSameFile_BrowserAndClientCaching also fails on net11 (1d364ca, 6d8743e). Cause not investigated.
- Test5Client Playwright goto timeout seen once. Cause not investigated.
- Not-in-scope cancelled runs: 40874896591, 40813664741.

LoadTest/performance ceiling numbers are noisy measurements; not treated as failures or analyzed here.
