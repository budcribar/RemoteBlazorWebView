#!/usr/bin/env bash
# Print VersionPrefix, VersionSuffix, and the NuGet version for one .NET line.
# Usage: derive-package-version.sh <net9|net10|net11> <repo-root> [version-override] [patch]
# Override, when set, is a full version such as 9.0.22 or 9.0.21-rc2.
# Patch is empty or 0 for none, or a positive integer that appends -pN.
# A GA SDK uses the committed Directory.Build.props version (9.0.21, 10.0.13),
# not {major}.0.0. A prerelease SDK still uses {major}.0.0 and a no-dot suffix.
# The script does not edit files. A hit on nuget.org is a warning, not a failure.
set -euo pipefail

line="${1:?line required}"
root="${2:?repo root required}"
override="${3:-}"
patch="${4:-}"

case "$line" in
  net9) major=9 ;;
  net10) major=10 ;;
  net11) major=11 ;;
  *) echo "Unknown line: $line" >&2; exit 2 ;;
esac

if [[ -n "$patch" && "$patch" != "0" ]]; then
  if [[ ! "$patch" =~ ^[1-9][0-9]*$ ]]; then
    echo "patch must be empty, 0, or a positive integer" >&2
    exit 2
  fi
else
  patch=""
fi

apply_patch() {
  if [[ -z "$patch" ]]; then
    return
  fi
  if [[ -n "$suffix" ]]; then
    suffix="${suffix}-p${patch}"
  else
    suffix="p${patch}"
  fi
}

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
  apply_patch
  sdk=""
else
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
  ga=1
  # SDK tokens use a dot (rc.2, preview.5). Published packages do not (rc2, preview5, pre).
  if [[ "$sdk" =~ -(preview|rc|alpha|beta|pre)\.([0-9]+) ]]; then
    suffix="${BASH_REMATCH[1]}${BASH_REMATCH[2]}"
    ga=0
  elif [[ "$sdk" =~ -(preview|rc|alpha|beta|pre)([0-9]+)([^0-9]|$) ]]; then
    suffix="${BASH_REMATCH[1]}${BASH_REMATCH[2]}"
    ga=0
  elif [[ "$sdk" =~ -(preview|rc|alpha|beta|pre)([^0-9A-Za-z]|$) ]]; then
    suffix="${BASH_REMATCH[1]}"
    ga=0
  fi

  # net9 and net10 publish patch versions from Directory.Build.props. A GA SDK
  # must not be rewritten to {major}.0.0.
  if [[ "$ga" == 1 ]]; then
    props="$root/Directory.Build.props"
    if [[ ! -f "$props" ]]; then
      echo "Directory.Build.props was not found under $root" >&2
      exit 2
    fi
    prefix=$(sed -n 's/.*<VersionPrefix>\([^<]*\)<\/VersionPrefix>.*/\1/p' "$props" | head -n 1)
    suffix=$(sed -n 's/.*<VersionSuffix>\([^<]*\)<\/VersionSuffix>.*/\1/p' "$props" | head -n 1)
    if [[ -z "$prefix" ]]; then
      echo "Directory.Build.props has no VersionPrefix" >&2
      exit 2
    fi
    if [[ "$suffix" =~ ^(preview|rc|alpha|beta|pre)\.([0-9]+)$ ]]; then
      suffix="${BASH_REMATCH[1]}${BASH_REMATCH[2]}"
    fi
    props_major=${prefix%%.*}
    if [[ "$props_major" != "$major" ]]; then
      echo "Line $line expects major $major but Directory.Build.props sets $prefix" >&2
      exit 2
    fi
  fi
  apply_patch
fi

if [[ -n "$suffix" ]]; then
  version="${prefix}-${suffix}"
else
  version="$prefix"
fi

echo "PREFIX=$prefix"
echo "SUFFIX=$suffix"
echo "VERSION=$version"
if [[ -n "$sdk" ]]; then
  echo "SDK=$sdk"
fi

# Read-only. https://api.nuget.org/v3-flatcontainer/<id>/index.json needs no key.
version_key=$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')
packages=(
  peakswc.remotewebview
  peakswc.remotewebviewservice
  peakswc.remoteblazorwebview.windowsforms
  peakswc.remoteblazorwebview.wpf
  peakswc.remoteblazorwebview
)
for id in "${packages[@]}"; do
  url="https://api.nuget.org/v3-flatcontainer/${id}/index.json"
  if ! body=$(curl -fsS "$url"); then
    echo "WARNING: could not read ${url}" >&2
    continue
  fi
  if printf '%s' "$body" | grep -Fq "\"${version_key}\""; then
    echo "WARNING: ${id} ${version} is already published on nuget.org. The next package must be a new version." >&2
  fi
done
