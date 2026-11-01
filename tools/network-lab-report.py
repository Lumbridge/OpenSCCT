#!/usr/bin/env python3
"""Index real-process lab evidence. Missing results are incomplete, never inferred passes."""
import argparse
import collections
import datetime
import json
from pathlib import Path
import re


def read_json(path, warnings):
    if not path.exists():
        return {}
    try:
        return json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        warnings.append(f"{path.name}: {error}")
        return {}


def collect(directory):
    warnings = []
    manifest = read_json(directory / "run.json", warnings)
    result = read_json(directory / "result.json", warnings)
    interruption = read_json(directory / "interrupted-by-operator.json", warnings)
    config = read_json(directory / "config.json", warnings)
    checkpoints = []
    progress = directory / "soak-progress.jsonl"
    if progress.exists():
        try:
            for index, line in enumerate(progress.read_text(encoding="utf-8-sig").splitlines(), 1):
                if not line.strip():
                    continue
                try:
                    checkpoints.append(json.loads(line))
                except ValueError:
                    warnings.append(f"soak-progress.jsonl: incomplete/invalid line {index}")
        except OSError as error:
            warnings.append(str(error))
    measurements = [m for point in checkpoints for m in point.get("measurements", [])]
    replicas = [r for measurement in measurements for r in measurement.get("replicas", [])]
    peak_pending = max((m.get("movement", {}).get("nextSequence", 0) -
                        m.get("movement", {}).get("acknowledged", 0) for m in measurements), default=None)
    peak_error = max((m.get("error", 0) for m in measurements), default=None)
    outcome = "PASS" if result.get("passed") is True else "FAIL" if result.get("passed") is False else "INCOMPLETE"
    recorded_outcome = outcome
    if interruption:
        outcome = "INTERRUPTED"
    visual = []
    for role in ("host", "guest", "observer", "observer2"):
        status = read_json(directory / f"{role}-status.json", warnings)
        if status.get("inspectedPoses"):
            visual.append({"role": role, "poses": status["inspectedPoses"], "issues": status.get("poseIssues") or []})
    areas = collections.Counter(issue.get("area", "unknown") for viewer in visual for issue in viewer["issues"])
    visual_flags = ", ".join(f"{area}: {count}" for area, count in sorted(areas.items())) if areas else "none reported" if visual else "not recorded"
    return {
        "run": directory.name, "path": directory.resolve().as_posix(), "outcome": outcome,
        "recordedOutcome": recorded_outcome, "operatorInterruption": interruption or None,
        "map": "Hub" if manifest.get("hub", config.get("hub")) else manifest.get("map", config.get("map")),
        "recordedMapArgument": manifest.get("map", config.get("map")),
        "scenario": result.get("scenario", manifest.get("scenario", "manual/unknown")),
        "profile": result.get("networkProfile", manifest.get("networkProfile", "unknown")),
        "mode": "Hub" if manifest.get("hub") else "dedicated" if manifest.get("dedicated") else "listen",
        "buildKind": "exported" if manifest.get("exported") else "project",
        "executable": manifest.get("executable"),
        "rendered": manifest.get("rendered", result.get("rendered", False)),
        "players": config.get("players"), "hostFps": manifest.get("hostFps"), "guestFps": manifest.get("guestFps"),
        "seed": manifest.get("seed"), "started": manifest.get("started"), "completed": result.get("completed"),
        "parameters": manifest.get("scenarioParameters", {}), "startup": result.get("startup"),
        "error": result.get("error"), "commit": manifest.get("commit"),
        "fingerprints": manifest.get("fingerprints", []),
        "visualFlags": visual_flags, "finalVisualDiagnostics": visual,
        "soak": {
            "checkpoints": len(checkpoints), "seconds": checkpoints[-1].get("seconds") if checkpoints else None,
            "cycles": checkpoints[-1].get("cycle") if checkpoints else None,
            "peakCheckpointPending": peak_pending, "peakCheckpointPositionError": peak_error,
            "peakCheckpointCorrections": max((m.get("movement", {}).get("corrections", 0) for m in measurements), default=None),
            "peakCheckpointResyncs": max((m.get("movement", {}).get("resyncs", 0) for m in measurements), default=None),
            "peakCheckpointMemoryBytes": max((m.get("memory", 0) for m in measurements), default=None),
            "replicaSamples": len(replicas),
            "peakCheckpointReplicaError": max((r["error"] for r in replicas), default=None),
        },
        "captures": sorted(p.resolve().as_posix() for p in (directory / "shots").glob("*.png")),
        "warnings": warnings,
    }


def cell(value):
    return str(value if value is not None else "—").replace("|", "\\|").replace("\n", " ")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parent.parent / "local/network-lab")
    parser.add_argument("--since", default="", help="Inclusive run-name prefix, e.g. 20261101-040000")
    parser.add_argument("--executable", type=Path, help="Only runs using this exact client executable; keeps candidate evidence separate")
    parser.add_argument("--output", type=Path, help="Output stem; writes .json and .md (default: ROOT/index)")
    args = parser.parse_args()
    if not args.root.is_dir():
        parser.error(f"Evidence root does not exist: {args.root}")
    runs = [collect(p) for p in sorted(args.root.iterdir())
            if p.is_dir() and re.fullmatch(r"\d{8}-\d{6}-[0-9a-f]+", p.name) and p.name >= args.since]
    if args.executable:
        selected = args.executable.resolve()
        runs = [run for run in runs if run["executable"] and Path(run["executable"]).resolve() == selected]
    counts = dict(collections.Counter(run["outcome"] for run in runs))
    generated = datetime.datetime.now(datetime.timezone.utc).isoformat()
    output = args.output or args.root / "index"
    output.parent.mkdir(parents=True, exist_ok=True)
    output.with_suffix(".json").write_text(json.dumps({"generated": generated, "counts": counts, "runs": runs}, indent=2) + "\n", encoding="utf-8")
    lines = ["# Network lab evidence index", "", f"Generated {generated}. Counts: {counts}.", "",
             f"Client executable filter: `{args.executable.resolve()}`." if args.executable else "Client executable filter: none; multiple build identities may be present.", "",
             "Each row is a separate local process run over real UDP. This is not physical PC/laptop validation.",
             "INCOMPLETE means no conclusive result file; it does not establish whether a process is still running.",
             "INTERRUPTED marks a documented operator stop; the original result/error is retained in JSON and is never counted as a pass.",
             "Result is scenario acceptance only. Visual flags independently retain the final inspector's findings; none reported is not full visual certification. Earlier scenes remain in the traces.",
             "Soak figures cover completed checkpoints, excluding any failed final interval. Full errors, parameters and build/content hashes are retained in the JSON index and run directories.", "",
             "| Run | Map / mode / build | Scenario / profile | Rendered / FPS caps | Result | Visual flags | Soak seconds | Max checkpoint pending |",
             "|---|---|---|---|---|---|---:|---:|"]
    for run in runs:
        seconds = run["soak"]["seconds"]
        lines.append(f"| [{run['run']}](<{run['path']}/result.json>) | {cell(run['map'])} / {run['mode']} / {run['buildKind']} | {cell(run['scenario'])} / {cell(run['profile'])} | {run['rendered']} / {cell(run['hostFps'])}:{cell(run['guestFps'])} | {run['outcome']} | {cell(run['visualFlags'])} | {round(seconds, 1) if seconds is not None else '—'} | {cell(run['soak']['peakCheckpointPending'])} |")
    failed = [run for run in runs if run["error"] or run["warnings"]]
    if failed:
        lines.extend(["", "## Failures and incomplete evidence", ""])
        for run in failed:
            lines.append(f"- **{run['run']}**: {cell(run['error'])}. {'; '.join(run['warnings'])}")
    output.with_suffix(".md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(json.dumps({"runs": len(runs), "counts": counts, "output": str(output.resolve())}))


if __name__ == "__main__":
    main()
