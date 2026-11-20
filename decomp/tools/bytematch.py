"""Export strict, source-backed matching totals using objdiff's v2 report format."""

import argparse
import json
from pathlib import Path

from progress import EDITIONS, ROOT, summary, write


def byte_measures(total, matched):
    return dict(total_code=str(total), matched_code=str(matched),
                matched_code_percent=100 * matched / total if total else 0)


def build_report(folder, target):
    counts = summary(folder, target)
    if counts["total_bytes"] is None:
        raise ValueError("This image has no measured code denominator yet")
    units = []
    described = 0
    for unit in target["units"]:
        size = unit["size"]
        verified = "match" in unit and unit.get("recipe_evidence") is not None
        measures = byte_measures(size, size if verified else 0)
        measures.update(total_functions=1, matched_functions=int(verified))
        source = next(path for path in unit["inputs"] if path.lower().endswith(".c"))
        function = dict(name=unit.get("rebuilt_symbol", unit["id"]), size=str(size),
                        fuzzy_match_percent=100 if verified else 0)
        units.append(dict(name=f'{folder.name}/{unit["id"]}', measures=measures, functions=[function],
                          metadata=dict(source_path=f"decomp/{folder.name}/{source}", complete=False)))
        described += size
    remainder = counts["total_bytes"] - described
    if remainder:
        units.append(dict(name=f"{folder.name}/unmapped-code", measures=byte_measures(remainder, 0)))
    return dict(version=2, measures=byte_measures(counts["total_bytes"], counts["matched_bytes"]),
                units=units)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("edition", choices=EDITIONS)
    parser.add_argument("target")
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    folder = ROOT / args.edition
    inputs = {path.stem: path for path in (folder / "config").glob("*.json")}
    if args.target not in inputs:
        parser.error("unknown target")
    result = build_report(folder, json.loads(inputs[args.target].read_text(encoding="utf-8")))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    write(args.output, result)
