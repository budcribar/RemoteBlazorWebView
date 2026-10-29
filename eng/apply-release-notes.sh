#!/usr/bin/env bash
# Write a notes file into PackageReleaseNotes in the five packable projects.
# Does not commit or push. The maintainer reviews the diff and commits it.
# Usage: apply-release-notes.sh <repo-root> <notes-file>
set -euo pipefail

root="${1:?repo root required}"
notes="${2:?notes file required}"

python3 - "$root" "$notes" << 'PY'
import pathlib, sys
root = pathlib.Path(sys.argv[1])
text = pathlib.Path(sys.argv[2]).read_text().strip()
escaped = text.replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")
files = [
    "src/RemoteWebView/RemoteWebView.csproj",
    "src/RemoteWebViewService/RemoteWebViewService.csproj",
    "src/RemoteBlazorWebView/RemoteBlazorWebView.csproj",
    "src/RemoteBlazorWebView.WinForms/RemoteBlazorWebView.WindowsForms.csproj",
    "src/RemoteBlazorWebView.Wpf/RemoteBlazorWebView.Wpf.csproj",
]
import re
pattern = re.compile(r"(<PackageReleaseNotes>)(.*?)(</PackageReleaseNotes>)", re.S)
for rel in files:
    path = root / rel
    original = path.read_text()
    updated, count = pattern.subn(lambda m: m.group(1) + escaped + m.group(3), original, count=1)
    if count != 1:
        raise SystemExit(f"PackageReleaseNotes was not found once in {rel}")
    path.write_text(updated)
    print(f"updated {rel}")
PY
