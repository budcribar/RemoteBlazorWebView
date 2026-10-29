#!/usr/bin/env bash
# Write package release notes from commits since the last tag.
# Omits subjects that state a performance result. Does not edit project files.
# Usage: release-notes.sh <repo-root> <output-file>
set -euo pipefail

root="${1:?repo root required}"
out="${2:?output file required}"

cd "$root"
if tag=$(git describe --tags --abbrev=0 2>/dev/null); then
  range="${tag}..HEAD"
  header="Changes since ${tag}"
else
  range="HEAD"
  header="Changes (no tag yet)"
fi

{
  echo "$header"
  echo
  git log "$range" --pretty=format:%s |
    grep -Eiv 'LoadTest|ceiling|req/s|requests per|throughput' || true
} > "$out"
