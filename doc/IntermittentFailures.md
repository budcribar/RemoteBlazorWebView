# Intermittent CI failures — net11

Generated 2026-10-29 (America/Denver) from `gh run list`/`gh run view --log` for runs 2026-10-15 → now. Logs: /workspace/vlogs/run-<id>.log.

## Open intermittent failures (no fix)

| Date (UTC / America/Denver) | Run | SHA | Workflow | Failing test / step | Error excerpt | Class | Evidence of later pass |
|---|---|---|---|---|---|---|---|
| 2026-10-26 01:48 UTC / 2026-10-25 19:48 MDT | [40255973476](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40255973476) | 1d364ca | Windows tests | ConcurrentRequestsTests.MultipleConcurrentFileRequestsWithSameFile_BrowserAndClientCaching; Local *.TestClicks(100) | TestClicks: System.InvalidOperationException : Failed to retrieve the WebSocket URL for Playwright to connect. | UNKNOWN | Next commit 505cdd3 [40251968974](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40251968974) passed |
| 2026-10-26 04:18 UTC / 2026-10-25 22:18 MDT | [40268415217](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40268415217) | 6d8743e | Windows tests | ConcurrentRequestsTests.MultipleConcurrentFileRequestsWithSameFile_BrowserAndClientCaching; Local *.TestClicks(100) | Error clicking 'Counter' link: Timeout 30000ms exceeded. | UNKNOWN (ConcurrentRequests test repeats) | Next commit 5dbccb3 [40272597939](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40272597939) passed |
| 2026-10-26 12:44 UTC / 2026-10-26 06:44 MDT | [40324836891](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40324836891) | 0140015 | Windows tests | LoadTest.FindMaxConcurrentRequests; Local *.TestClicks(100) (6 variants) | Failed!  - Failed: 1, Passed: 18 (LoadTest) + TestClicks failures | UNKNOWN | Next commit bd7b786 "Launch the load test with the same Chromium flags…" [40333271813](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40333271813) passed |

## Fixed failures

| Date (UTC / America/Denver) | Run | SHA | Workflow | Failing test / step | Error excerpt | Class | Evidence of later pass |
|---|---|---|---|---|---|---|---|
| 2026-10-25 18:45 UTC / 2026-10-25 12:45 MDT | [40222793550](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40222793550) | 7c28b12 | Windows tests | Workflow PowerShell step | Cannot find path 'D:\a\…\src\RemoteWebView…' because it does not exist. | FIXED (workflow bring-up) | Later commits passed |
| 2026-10-25 18:49 UTC / 2026-10-25 12:49 MDT | [40223068260](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40223068260) | 68b0c94 | Windows tests | Workflow PowerShell step | Cannot overwrite the item D:\a\…\src\RemoteWebView… | FIXED (workflow bring-up) | Later commits passed |
| 2026-10-25 19:57 UTC / 2026-10-25 13:57 MDT | [40228499433](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40228499433) | 1509f38 | Windows tests | RemoteStaticFileHeaderTests.Curl_against_kestrel_shows_the_provider_header; PerformanceTests.FetchLargeFiles_PerformanceMetrics; many Local/Remote tests | Failed Curl_against_kestrel_shows_the_provider_header [277 ms] | FIXED (curl: 6d36f2a) / others UNKNOWN | 41b0a99 [40230982040](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40230982040) passed |
| 2026-10-25 20:13 UTC / 2026-10-25 14:13 MDT | [40229744125](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40229744125) | 6d36f2a | Windows tests | PerformanceTests.FetchLargeFiles_PerformanceMetrics; TestRemoteBlazorWinFormsDebug.* etc. | Failed FetchLargeFiles_PerformanceMetrics [13 s] | UNKNOWN (bring-up period) | 41b0a99 [40230982040](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40230982040) passed |
| 2026-10-28 20:22 UTC / 2026-10-28 14:22 MDT | [40762691189](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40762691189) | 7fee382 | SonarCloud | EndToEnd Chrome module tests (3) | Failed!  - Failed: 3, Passed: 48 - StaticContentCache.EndToEnd.Tests.dll | FIXED (probable; same Chrome E2E family) | 84aad20 Sonar [40876544202](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40876544202) passed |
| 2026-10-28 23:52 UTC / 2026-10-28 17:52 MDT | [40790314520](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40790314520) | 398f2b9 | Windows tests | EndToEnd 4 Chrome module tests | TaskCanceledException: The request was canceled due to the configured HttpClient.Timeout of 5 seconds elapsing. | FIXED | 106aa77 [40803001983](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40803001983) passed |
| 2026-10-29 00:15 UTC / 2026-10-28 18:15 MDT | [40792847265](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40792847265) | df44292 | Windows tests | TestRemoteBlazorWinFormsDebug.* (4); TestLocalBlazorWebViewDebug.TestClicks | FileNotFoundException: Application executable not found … net11.0-windows | FIXED (path config) | 106aa77 [40803001983](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40803001983) passed |
| 2026-10-29 00:57 UTC / 2026-10-28 18:57 MDT | [40797421613](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40797421613) | 5976f96 | SonarCloud | EndToEnd Test_hook_sees_each_initializer…; Throwing_hook_leaves… | Failed!  - Failed: 2, Passed: 49 | FIXED (probable); this SHA passed Sonar earlier in [40787689873](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40787689873) | 106aa77 [40803001926](https://github.com/budcribar/RemoteBlazorWebView/actions/runs/40803001926) and 84aad20 passed |

### Playbook: Chrome DevTools E2E family (398f2b9, 5976f96, 7fee382)
- 106aa77, "Retry a timed-out DevTools probe and keep the Chrome module classes in one collection." Its message says: "HttpClient's 5s timeout was escaping the existing 15s debugger wait. The three classes that launch Chrome now share a collection so they do not start together." Log evidence: in run 40790314520, `HttpClient.Timeout of 5 seconds elapsing`.
- 84aad20 is the net11 copy of the dedicated Chrome stdout reader thread fix (see net10 playbook: 5d0d6e4). The Sonar failures (7fee382, 5976f96) belong to the same test family, and 5976f96 passed once and failed once on the same SHA. Linking them to these fixes is an inference: no failure has appeared since, but no direct proof was gathered.

### Playbook: Test2Client5Refresh exit/refresh race
- Fix: 5b1ed7d "Drop late browser messages after a client is removed, and treat a replaced registration as shutdown." Its message says: "A dying page can no longer recreate its registry entry, and the refresh wait returns when that entry is a different object." The diff (verified):
  - BrowserIPCService.cs creates the entry only when `request.IsPrimary && request.Sequence == 1`. Any other late message logs "Dropped browser SendMessage…".
  - StartOrRefresh.cs adds a `!ReferenceEquals(current, original)` check.
  - It adds test/StaticContentCache.ServiceTests/RefreshRaceTests.cs, a test-first regression test.
- Debugging approach: the branch `net10-race-debug` holds temporary diagnostic commits: 22a63fc "Log refresh shutdown ordering to isolate the restart race", 7150132 "Record refresh ordering in an in-memory ring", 0ffaa17, a15c6ff (sequence recorded on registry insert/deregister), and a71cae5 "Repeat Test2Client5Refresh on the race-debug workflow". After the fix, 10 repeated Windows runs on 5b1ed7d (2026-10-27 02:11–08:58 UTC) all passed.
- Scope note: no Test2Client5Refresh race failure appears in the net11 failure logs from this window. The race failure logs themselves are on the debug branch and were not read here.

### Playbook: build-path breaks (df44292 and bring-up)
- When several tests fail within about 4 s with `Application executable not found` or `Cannot find path`, the cause is the path or build config, not a flake. Fix the path, then confirm with one green run.

## Pattern summary

- Recurring tests:
  - ConcurrentRequestsTests.MultipleConcurrentFileRequestsWithSameFile_BrowserAndClientCaching failed on 1d364ca and 6d8743e, and on net9 bb91014.
  - Local *.TestClicks(numClicks: 100) failed in 1d364ca, 6d8743e and 0140015, with varying errors (WebSocket URL, 30 s click timeout).
  - Chrome E2E module tests failed 3 times; probably fixed.
  - Cause not investigated for ConcurrentRequests and TestClicks.
- The '.NET' workflow failed on every run 10-17 through 10-25 (out of scope; not analyzed).

LoadTest/performance ceiling numbers are noisy measurements; not treated as failures or analyzed here.
