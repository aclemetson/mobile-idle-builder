"""Compare AI playtest benchmark runs (Playtests/benchmarks/<scenario>/<run>.json).

  python scripts/benchmark-report.py                 latest run of every scenario, side by side
  python scripts/benchmark-report.py casual-idler    every run of one scenario, oldest first
  options: --all (every milestone, not just key ones), --csv (comma-separated output)

Cells are player time (h:mm:ss) when the run first reached the milestone; '-' = never reached.
"""
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent / "Playtests" / "benchmarks"

KEY_TYPES = {
    "tutorial_complete", "research_done", "building_first", "entropy", "net_worth", "tier",
    "prestige_available", "prestige", "site_unlocked", "world_unlocked",
}
ONE_OFF_TYPES = {"run_start", "run_end", "mark", "purchase", "away", "crystals_spent", "research_started"}


def load_runs(scenario_dir):
    runs = []
    for path in sorted(scenario_dir.glob("*.json")):
        try:
            runs.append(json.loads(path.read_text(encoding="utf-8")))
        except (OSError, ValueError) as e:
            print(f"skipping {path}: {e}", file=sys.stderr)
    return runs


def milestone_times(run, include_all):
    times = {}
    for m in run.get("milestones", []):
        kind = m.get("type", "")
        if kind in ONE_OFF_TYPES:
            continue
        if not include_all and kind not in KEY_TYPES:
            continue
        key = f"{kind} {m.get('detail', '')}".strip()
        times.setdefault(key, m.get("playerSeconds", 0.0))
    return times


def hms(seconds):
    if seconds is None:
        return "-"
    s = int(round(seconds))
    return f"{s // 3600}:{(s % 3600) // 60:02d}:{s % 60:02d}"


def main(argv):
    include_all = "--all" in argv
    as_csv = "--csv" in argv
    names = [a for a in argv if not a.startswith("--")]

    if not ROOT.exists():
        print(f"No benchmarks yet ({ROOT}).")
        return 0

    columns = []  # (label, run)
    if names:
        for run in load_runs(ROOT / names[0]):
            columns.append((f"{run.get('runId')}@{run.get('commit') or '?'}", run))
    else:
        for scenario_dir in sorted(p for p in ROOT.iterdir() if p.is_dir()):
            runs = load_runs(scenario_dir)
            if runs:
                columns.append((scenario_dir.name, runs[-1]))
    if not columns:
        print("No runs found.")
        return 0

    per_run = [milestone_times(run, include_all) for _, run in columns]
    keys = sorted({k for t in per_run for k in t},
                  key=lambda k: min(t[k] for t in per_run if k in t))

    header = ["milestone"] + [label for label, _ in columns]
    rows = [[k] + [hms(t.get(k)) for t in per_run] for k in keys]
    rows.append(["-- total player time"] + [hms(run.get("playerSeconds")) for _, run in columns])
    rows.append(["-- agent actions"] + [str(run.get("actions", "-")) for _, run in columns])
    rows.append(["-- dollars spent"] + [f"${run.get('dollarsSpent', 0):.2f}" for _, run in columns])
    rows.append(["-- outcome"] + [str(run.get("outcome", "-")) for _, run in columns])
    rows.append(["-- app version"] + [str(run.get("appVersion", "-")) for _, run in columns])

    if as_csv:
        for row in [header] + rows:
            print(",".join(f'"{c}"' for c in row))
        return 0

    widths = [max(len(r[i]) for r in [header] + rows) for i in range(len(header))]
    for row in [header] + rows:
        print("  ".join(c.ljust(w) for c, w in zip(row, widths)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
