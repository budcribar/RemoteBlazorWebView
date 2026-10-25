#!/usr/bin/env python3
"""Median-compare baseline and current perf-regression JSON samples.

Counts and payload sizes warn above --count-tolerance (default 5%).
elapsedMs and allocatedBytes warn above --time-tolerance (default 20%).
allocatedBytes uses the wider tolerance because the total includes Kestrel
threads. The process always exits 0; WARN is a column, not a failed push.
"""

from __future__ import annotations

import argparse
import os
import json
import statistics
from pathlib import Path

TIME_METRICS = {"elapsedMs", "allocatedBytes"}


def load_samples(directory: Path) -> list[dict]:
    samples = []
    for path in sorted(directory.glob("*.json")):
        text = path.read_text(encoding="utf-8", errors="replace")
        parsed = None
        for line in reversed(text.splitlines()):
            line = line.strip()
            if not line.startswith("{"):
                continue
            try:
                parsed = json.loads(line)
            except json.JSONDecodeError:
                continue
            break
        if parsed is None:
            samples.append({"error": f"{path.name}: no JSON object"})
        else:
            samples.append(parsed)
    return samples


def scenario_map(sample: dict) -> dict[str, dict[str, float]]:
    found = {}
    for scenario in sample.get("scenarios", []):
        name = scenario.get("name")
        metrics = scenario.get("metrics") or {}
        if name:
            found[name] = {key: float(value) for key, value in metrics.items()}
    return found


def median(values: list[float]) -> float:
    return statistics.median(values)


def ratio(current: float, baseline: float) -> float | None:
    if baseline == 0:
        return 0.0 if current == 0 else None
    return current / baseline


def flag_for(metric: str, current: float, baseline: float, time_tolerance: float, count_tolerance: float) -> tuple[str, str]:
    change = ratio(current, baseline)
    if change is None:
        return "WARN", "inf"
    tolerance = time_tolerance if metric in TIME_METRICS else count_tolerance
    shown = f"{change:.3f}"
    if change > 1 + tolerance:
        return "WARN", shown
    return "PASS", shown


def fmt(value: float) -> str:
    if abs(value - round(value)) < 1e-6 and abs(value) < 1e12:
        return str(int(round(value)))
    return f"{value:.3f}"


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--baseline", type=Path, required=True)
    parser.add_argument("--current", type=Path, required=True)
    parser.add_argument("--json-out", type=Path, required=True)
    parser.add_argument("--md-out", type=Path, required=True)
    parser.add_argument("--time-tolerance", type=float, default=0.20)
    parser.add_argument("--count-tolerance", type=float, default=0.05)
    parser.add_argument("--baseline-ref", default="")
    args = parser.parse_args()

    baseline_samples = [sample for sample in load_samples(args.baseline) if "scenarios" in sample]
    current_samples = [sample for sample in load_samples(args.current) if "scenarios" in sample]
    notes = []
    if not baseline_samples or not current_samples:
        notes.append(
            f"Could not compare: baseline samples={len(baseline_samples)}, current samples={len(current_samples)}."
        )

    rows = []
    names = []
    for sample in baseline_samples + current_samples:
        for name in scenario_map(sample):
            if name not in names:
                names.append(name)

    for name in names:
        base_metrics = [scenario_map(sample).get(name) for sample in baseline_samples]
        cur_metrics = [scenario_map(sample).get(name) for sample in current_samples]
        base_present = [item for item in base_metrics if item]
        cur_present = [item for item in cur_metrics if item]
        if not base_present or not cur_present:
            rows.append({
                "scenario": name,
                "metric": "(scenario)",
                "baseline": len(base_present),
                "current": len(cur_present),
                "ratio": "n/a",
                "flag": "SKIP",
                "note": "Present on one side only. A new scenario is reported and not treated as a regression.",
            })
            continue
        keys = []
        for metrics in base_present + cur_present:
            for key in metrics:
                if key not in keys:
                    keys.append(key)
        for key in keys:
            base_values = [metrics[key] for metrics in base_present if key in metrics]
            cur_values = [metrics[key] for metrics in cur_present if key in metrics]
            if not base_values or not cur_values:
                rows.append({
                    "scenario": name,
                    "metric": key,
                    "baseline": "n/a" if not base_values else fmt(median(base_values)),
                    "current": "n/a" if not cur_values else fmt(median(cur_values)),
                    "ratio": "n/a",
                    "flag": "SKIP",
                })
                continue
            base_median = median(base_values)
            cur_median = median(cur_values)
            status, shown = flag_for(key, cur_median, base_median, args.time_tolerance, args.count_tolerance)
            rows.append({
                "scenario": name,
                "metric": key,
                "baseline": base_median,
                "current": cur_median,
                "ratio": shown,
                "flag": status,
            })

    current_medians = {}
    for name in names:
        present = [item for item in (scenario_map(sample).get(name) for sample in current_samples) if item]
        if not present:
            continue
        keys = []
        for metrics in present:
            for key in metrics:
                if key not in keys:
                    keys.append(key)
        current_medians[name] = {
            key: median([metrics[key] for metrics in present if key in metrics])
            for key in keys
            if any(key in metrics for metrics in present)
        }

    warns = sum(1 for row in rows if row["flag"] == "WARN")
    payload = {
        "baselineRef": args.baseline_ref,
        "baselineSamples": len(baseline_samples),
        "currentSamples": len(current_samples),
        "timeTolerance": args.time_tolerance,
        "countTolerance": args.count_tolerance,
        "warns": warns,
        "notes": notes,
        "rows": rows,
        "framework": os.environ.get("PERF_FRAMEWORK", ""),
        "currentMedians": current_medians,
    }
    args.json_out.parent.mkdir(parents=True, exist_ok=True)
    args.json_out.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")

    lines = [
        "# Performance regression",
        "",
        f"Baseline `{args.baseline_ref or args.baseline}` ({len(baseline_samples)} samples) vs current ({len(current_samples)} samples).",
        f"Timing and allocation tolerance: {args.time_tolerance:.0%} slower. Count and byte tolerance: {args.count_tolerance:.0%} increase.",
        "",
        "| Scenario | Metric | Baseline | Current | Ratio | Flag |",
        "| --- | --- | ---: | ---: | ---: | --- |",
    ]
    for row in rows:
        base = row["baseline"] if isinstance(row["baseline"], str) else fmt(row["baseline"])
        cur = row["current"] if isinstance(row["current"], str) else fmt(row["current"])
        lines.append(f"| {row['scenario']} | {row['metric']} | {base} | {cur} | {row['ratio']} | {row['flag']} |")
    if notes:
        lines.extend(["", *[f"- {note}" for note in notes]])
    if warns:
        lines.extend(["", f"{warns} metric(s) flagged WARN. This check does not fail the push."])
    else:
        lines.extend(["", "No metric exceeded its tolerance."])
    markdown = "\n".join(lines) + "\n"
    args.md_out.write_text(markdown, encoding="utf-8")
    print(markdown, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
