#!/usr/bin/env bash
# Print VersionPrefix and VersionSuffix for one .NET line.
# Usage: derive-package-version.sh <net9|net10|net11> <repo-root> [version-override]
# Override, when set, is a full NuGet version such as 11.0.1 or 11.0.0-rc.2.
# The script does not edit files.
set -euo pipefail

line="${1:?line required}"
root="${2:?repo root required}"
override="${3:-}"

case "$line" in
  net9) major=9 ;;
  net10) major=10 ;;
  net11) major=11 ;;
  *) echo "Unknown line: $line" >&2; exit 2 ;;
esac

if [[ -n "$override" ]]; then
  if [[ ! "$override" =~ ^[0-9A-Za-z.+-]+$ ]]; then
    echo "version override must be a single version token" >&2
    exit 2
  fi
  if [[ "$override" == *-* ]]; then
    prefix="${override%%-*}"
    suffix="${override#*-}"
  else
    prefix="$override"
    suffix=""
  fi
  echo "PREFIX=$prefix"
  echo "SUFFIX=$suffix"
  echo "VERSION=$override"
  exit 0
fi

sdk_file="$root/global.json"
if [[ ! -f "$sdk_file" ]]; then
  echo "global.json was not found under $root" >&2
  exit 2
fi

sdk=$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$sdk_file" | head -n 1)
if [[ -z "$sdk" ]]; then
  echo "global.json has no sdk version" >&2
  exit 2
fi

sdk_major=${sdk%%.*}
if [[ "$sdk_major" != "$major" ]]; then
  echo "Line $line expects major $major but global.json pins $sdk" >&2
  exit 2
fi

prefix="${major}.0.0"
suffix=""
if [[ "$sdk" =~ -(preview|rc|alpha|beta)\.([0-9]+) ]]; then
  suffix="${BASH_REMATCH[1]}.${BASH_REMATCH[2]}"
fi

echo "PREFIX=$prefix"
echo "SUFFIX=$suffix"
if [[ -n "$suffix" ]]; then
  echo "VERSION=${prefix}-${suffix}"
else
  echo "VERSION=$prefix"
fi
echo "SDK=$sdk"
