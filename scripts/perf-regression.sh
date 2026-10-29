#!/usr/bin/env bash
# Build the current tree and a baseline worktree, run PerfRegression interleaved
# on this machine, and write a median ratio table. A slow timing is a WARN and
# does not fail the run. A failed build or zero samples on either side exits 1.
set -u

ROOT=$(cd "$(dirname "$0")/.." && pwd)
cd "$ROOT"

REPEATS=${REPEATS:-5}
TIME_TOLERANCE=${TIME_TOLERANCE:-0.20}
COUNT_TOLERANCE=${COUNT_TOLERANCE:-0.05}
OUT=${OUT:-"$ROOT/perf-results"}
WORKTREE=${WORKTREE:-"${RUNNER_TEMP:-/tmp}/perf-baseline"}

if ! [[ $REPEATS =~ ^[0-9]+$ ]] || [[ $REPEATS -lt 1 ]]; then
  REPEATS=5
fi
if [[ $REPEATS -gt 20 ]]; then
  REPEATS=20
fi
if ! [[ $TIME_TOLERANCE =~ ^[0-9]+([.][0-9]+)?$ ]]; then
  TIME_TOLERANCE=0.20
fi
if ! [[ $COUNT_TOLERANCE =~ ^[0-9]+([.][0-9]+)?$ ]]; then
  COUNT_TOLERANCE=0.05
fi

resolve_baseline() {
  # BASELINE is the workflow input/env. Empty means origin/net10, the same
  # ref on net9, net10, and net11. Tags are not considered.
  if [[ -n ${BASELINE:-} ]]; then
    printf '%s\n' "$BASELINE"
    return
  fi
  printf '%s\n' origin/net10
}

# npm's protoc shim and some plugin bins ship a CRLF shebang. Linux refuses
# to exec them (`/usr/bin/env: ‘node\r’`). The repo only vendors protoc.exe,
# so Linux has to use those shims.
fix_crlf_shebangs() {
  local root=$1
  [[ -d $root ]] || return 0
  python3 - "$root" <<'PY'
import os, sys
root = sys.argv[1]
for dirpath, _dirnames, filenames in os.walk(root):
    for name in filenames:
        path = os.path.join(dirpath, name)
        try:
            with open(path, "rb") as handle:
                first = handle.readline(256)
                if not first.startswith(b"#!") or b"\r" not in first or b"\0" in first:
                    continue
                rest = handle.read()
        except OSError:
            continue
        data = first + rest
        if b"\0" in data[:512]:
            continue
        with open(path, "wb") as handle:
            handle.write(data.replace(b"\r\n", b"\n").replace(b"\r", b"\n"))
PY
}

build_js() {
  local js=src/RemoteWebView.Blazor.JS/dist/remote.blazor.desktop.js
  if [[ -f $js ]]; then
    return 0
  fi
  corepack enable
  corepack prepare yarn@4.3.1 --activate
  export PATH="$ROOT/src/RemoteWebView.Blazor.JS/protoc/bin:$PATH"
  pushd src/RemoteWebView.Blazor.JS >/dev/null
  yarn install --immutable || { popd >/dev/null; return 1; }
  pushd Web.JS >/dev/null
  npm install --no-audit --no-fund || { popd >/dev/null; popd >/dev/null; return 1; }
  popd >/dev/null
  fix_crlf_shebangs node_modules
  fix_crlf_shebangs Web.JS/node_modules
  npm run build:production || { popd >/dev/null; return 1; }
  popd >/dev/null
}

tfm_of() {
  local file=$1/src/RemoteWebView/RemoteWebView.csproj
  sed -n 's/.*<TargetFramework>\(.*\)<\/TargetFramework>.*/\1/p' "$file" | head -1
}

build_harness() {
  local root=$1
  local log=$2
  local tfm dll
  tfm=$(tfm_of "$root")
  dll="$root/src/Benchmarks/PerfRegression/bin/x64/Release/$tfm/PerfRegression.dll"
  # The host resolves global.json from the working directory. Building the
  # net10 baseline from this net9 tree would keep the 9.0 SDK selected, and
  # that SDK cannot target net10.0.
  if ! ( cd "$root" && dotnet build "$root/src/Benchmarks/PerfRegression/PerfRegression.csproj" \
    -c Release \
    -p:Platform=x64 \
    -p:PerfTfm="$tfm" \
    -p:GeneratePackageOnBuild=false \
    -p:EnableWindowsTargeting=true \
    --nologo ) >"$log" 2>&1; then
    return 1
  fi
  if [[ ! -f $dll ]]; then
    echo "missing output $dll" >>"$log"
    return 1
  fi
  printf '%s\n' "$dll"
}

copy_harness() {
  local root=$1
  local dest=$root/src/Benchmarks/PerfRegression
  rm -rf "$dest"
  mkdir -p "$dest" "$root/src/RemoteWebView.Blazor.JS/dist"
  cp src/Benchmarks/PerfRegression/PerfRegression.csproj \
    src/Benchmarks/PerfRegression/Program.cs \
    "$dest/"
  cp -f src/RemoteWebView.Blazor.JS/dist/remote.blazor.desktop.js \
    "$root/src/RemoteWebView.Blazor.JS/dist/remote.blazor.desktop.js"
}

cleanup() {
  git worktree remove --force "$WORKTREE" >/dev/null 2>&1 || true
}
trap cleanup EXIT

write_summary() {
  python3 "$ROOT/scripts/compare-perf.py" \
    --baseline "$OUT/baseline" \
    --current "$OUT/current" \
    --baseline-ref "$BASELINE_REF" \
    --time-tolerance "$TIME_TOLERANCE" \
    --count-tolerance "$COUNT_TOLERANCE" \
    --json-out "$OUT/summary.json" \
    --md-out "$OUT/summary.md" || true
  if [[ -n ${GITHUB_STEP_SUMMARY:-} && -f $OUT/summary.md ]]; then
    cat "$OUT/summary.md" >> "$GITHUB_STEP_SUMMARY"
  fi
}

sample_count() {
  python3 - "$1" <<'PY'
import json, sys
from pathlib import Path
count = 0
root = Path(sys.argv[1])
if root.is_dir():
    for path in sorted(root.glob("*.json")):
        parsed = None
        for line in reversed(path.read_text(encoding="utf-8", errors="replace").splitlines()):
            line = line.strip()
            if not line.startswith("{"):
                continue
            try:
                parsed = json.loads(line)
            except json.JSONDecodeError:
                continue
            break
        if isinstance(parsed, dict) and "scenarios" in parsed:
            count += 1
print(count)
PY
}

finish() {
  local status=$1
  local baseline_samples current_samples
  write_summary
  baseline_samples=$(sample_count "$OUT/baseline")
  current_samples=$(sample_count "$OUT/current")
  if [[ $baseline_samples -eq 0 || $current_samples -eq 0 ]]; then
    echo "Zero samples: baseline=${baseline_samples} current=${current_samples}. This check fails when a build fails or either side has no samples. A slow timing does not fail it." | tee -a "$OUT/error.txt"
    status=1
  fi
  if [[ $status -ne 0 ]]; then
    echo "Performance regression failed. See perf-results/error.txt." >&2
    exit 1
  fi
  exit 0
}

mkdir -p "$OUT/baseline" "$OUT/current"
rm -f "$OUT/baseline/"*.json "$OUT/current/"*.json "$OUT/build-baseline.log" "$OUT/build-current.log" "$OUT/error.txt"

BASELINE_REF=$(resolve_baseline)
echo "Baseline ref: $BASELINE_REF" | tee "$OUT/baseline-ref.txt"
git fetch origin net10 --quiet || true

if ! build_js; then
  echo "JS bundle build failed" | tee "$OUT/error.txt"
  finish 1
fi

if [[ ! -f src/RemoteWebView.Blazor.JS/dist/remote.blazor.desktop.js ]]; then
  echo "remote.blazor.desktop.js is missing, so the harness cannot be built." | tee "$OUT/error.txt"
  finish 1
fi

git worktree remove --force "$WORKTREE" >/dev/null 2>&1 || true
if ! git worktree add --detach "$WORKTREE" "$BASELINE_REF"; then
  echo "Could not check out baseline $BASELINE_REF" | tee "$OUT/error.txt"
  finish 1
fi
copy_harness "$WORKTREE"

current_status=0
baseline_status=0
current_dll=$(build_harness "$ROOT" "$OUT/build-current.log") || current_status=$?
baseline_dll=$(build_harness "$WORKTREE" "$OUT/build-baseline.log") || baseline_status=$?
build_status=0
if [[ $current_status -ne 0 || $baseline_status -ne 0 ]]; then
  echo "Harness build failed (current=$current_status baseline=$baseline_status). See perf-results/build-*.log." | tee "$OUT/error.txt"
  build_status=1
fi

if [[ $current_status -eq 0 || $baseline_status -eq 0 ]]; then
  for i in $(seq 1 "$REPEATS"); do
    if [[ $baseline_status -eq 0 ]]; then
      if ! dotnet "$baseline_dll" > "$OUT/baseline/$i.json" 2>>"$OUT/baseline/stderr.log"; then
        echo "{\"error\":\"baseline $i failed\"}" > "$OUT/baseline/$i.json"
      fi
    fi
    if [[ $current_status -eq 0 ]]; then
      if ! dotnet "$current_dll" > "$OUT/current/$i.json" 2>>"$OUT/current/stderr.log"; then
        echo "{\"error\":\"current $i failed\"}" > "$OUT/current/$i.json"
      fi
    fi
  done
fi

finish "$build_status"
