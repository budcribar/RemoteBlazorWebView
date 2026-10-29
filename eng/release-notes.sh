#!/usr/bin/env bash
# Write package release notes: a "Based on maui <version>" line, then commit
# subjects since the last tag. Omits subjects that state a performance result.
# Truncates with a compare link so PackageReleaseNotes stays short.
# Does not edit project files.
# Usage: release-notes.sh <repo-root> <output-file> <maui-version>
set -euo pipefail

root="${1:?repo root required}"
out="${2:?output file required}"
version="${3:?maui version required}"
limit=4000

cd "$root"
if tag=$(git describe --tags --abbrev=0 2>/dev/null); then
  range="${tag}..HEAD"
  header="Changes since ${tag}"
  sha=$(git rev-parse HEAD)
  link="https://github.com/budcribar/RemoteBlazorWebView/compare/${tag}...${sha}"
else
  range="HEAD"
  header="Changes (no tag yet)"
  link="https://github.com/budcribar/RemoteBlazorWebView/commits/HEAD"
fi

mapfile -t subjects < <(git log "$range" --pretty=tformat:%s | grep -Eiv 'LoadTest|ceiling|req/s|requests per|throughput' || true)

body="Based on maui ${version}"$'\n\n'"${header}"
truncated=0
for subject in "${subjects[@]}"; do
  [[ -z "$subject" ]] && continue
  candidate="${body}"$'\n'"${subject}"
  footer=$'\n\n'"Full list: ${link}"
  if (( ${#candidate} + ${#footer} > limit )); then
    body="${body}"$'\n\n'"Full list: ${link}"
    truncated=1
    break
  fi
  body="$candidate"
done

if (( truncated == 0 && ${#body} > limit )); then
  body="${body:0:limit}"$'\n\n'"Full list: ${link}"
fi

printf '%s\n' "$body" > "$out"
