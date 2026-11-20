"""Create reproducible, per-executable matching reports using Python's standard library."""

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
from images import coff_symbol_offset, inspect, span


ROOT = Path(__file__).resolve().parents[1]
EDITIONS = ("ps2", "pc")


def encoded(obj):
    return json.dumps(obj, indent=2, ensure_ascii=False) + "\n"


def write(path, value):
    path.write_text(encoded(value), encoding="utf-8", newline="\n")


def identity(value):
    if not isinstance(value, str) or not re.fullmatch("[a-z0-9][a-z0-9-]*", value):
        raise ValueError(f"invalid identifier: {value!r}")
    return value


def digest(value):
    if not isinstance(value, str) or not re.fullmatch("[a-f0-9]{64}", value):
        raise ValueError("expected a lowercase SHA-256 digest")
    return value


def spans(records, limit):
    ordered = sorted(span(r["offset"], r["size"], limit) for r in records)
    if any(a[1] > b[0] for a, b in zip(ordered, ordered[1:])):
        raise ValueError("overlapping ranges would double count progress")
    return ordered


def contains(ranges, bounds):
    return any(begin <= bounds[0] and bounds[1] <= end for begin, end in ranges)


def inputs_digest(folder, unit, image_hash):
    inputs = unit["inputs"]
    if not isinstance(inputs, list) or not any(Path(p).suffix.lower() == ".c" for p in inputs):
        raise ValueError("a matching unit requires its independently written C source")
    if not unit["toolchain"].strip() or not unit["build_command"].strip():
        raise ValueError("record the compiler version and reproducible build command")
    evidence = unit.get("recipe_evidence")
    if evidence is not None:
        if (evidence["compiler"] != unit["toolchain"] or not isinstance(evidence["flags"], list)
                or not all(isinstance(flag, str) and flag for flag in evidence["flags"])):
            raise ValueError("record the original compiler and flags for this unit")
        basis = evidence["basis"]
        if Path(basis).suffix.lower() != ".md":
            raise ValueError("original compiler and flag evidence requires a reviewed Markdown note")
        inputs = [*inputs, basis]
    fingerprints = {}
    for relative in sorted(inputs):
        path = (folder / relative).resolve()
        if not path.is_relative_to(folder.resolve()) or Path(relative).is_absolute():
            raise ValueError("build inputs must be project-relative")
        data = path.read_bytes().replace(b"\r\n", b"\n")
        if evidence and relative == evidence["basis"] and not data.strip():
            raise ValueError("original compiler and flag evidence is empty")
        fingerprints[relative] = hashlib.sha256(data).hexdigest()
    recipe = {k: v for k, v in unit.items() if k != "match"}
    payload = json.dumps([image_hash, recipe, fingerprints], sort_keys=True).encode()
    return hashlib.sha256(payload).hexdigest()


def summary(folder, target):
    image = target["image"]
    image_hash = digest(image["sha256"])
    expected_format = {"pc": "pe32-x86", "ps2": "elf32-mipsel"}[folder.name]
    if image["format"] != expected_format:
        raise ValueError("image format belongs to another edition")
    all_sections = image["executable_sections"]
    executable = spans(all_sections, image["size"])
    if not executable or any(type(s["code"]) is not bool for s in all_sections):
        raise ValueError("invalid executable-section inventory")
    code = spans([s for s in all_sections if s["code"]], image["size"])
    # RWX sections on the PS2 discs combine code and data. A reviewed range map
    # can replace section inference once all the code in the image is identified.
    complete = bool(code) and (folder.name == "pc" or all(s["code"] for s in all_sections))
    if "code_map" in target:
        mapped = target["code_map"]
        code = spans(mapped["ranges"], image["size"])
        if not code or not mapped["basis"].strip() or not all(contains(executable, r) for r in code):
            raise ValueError("code map needs evidence and file-backed executable ranges")
        complete = True
    total = sum(end - begin for begin, end in code) if complete else None
    units = target["units"]
    ranges = spans(units, image["size"])
    if not all(contains(code if complete else executable, r) for r in ranges):
        raise ValueError("unit outside the target's measured code or executable sections")
    if len({identity(u["id"]) for u in units}) != len(units):
        raise ValueError("duplicate unit id")
    matched_bytes = matched_units = candidate_bytes = candidate_units = 0
    for unit in units:
        if "rebuilt_symbol" in unit:
            if "rebuilt_offset" in unit or not unit["rebuilt_symbol"]:
                raise ValueError("choose one rebuilt artifact location")
        elif type(unit.get("rebuilt_offset")) is not int or unit["rebuilt_offset"] < 0:
            raise ValueError("missing or invalid rebuilt offset")
        signature = inputs_digest(folder, unit, image_hash)
        if "match" in unit:
            if unit["match"]["inputs_sha256"] != signature:
                raise ValueError(f"{unit['id']}: changed build inputs; reverify this unit")
            digest(unit["match"]["bytes_sha256"])
            if unit.get("recipe_evidence") is not None:
                matched_bytes += unit["size"]
                matched_units += 1
            else:
                candidate_bytes += unit["size"]
                candidate_units += 1
    return dict(id=identity(target["id"]), label=target["label"], scope=target["scope"],
                sha256=image_hash, matched_bytes=matched_bytes, total_bytes=total,
                matched_units=matched_units, candidate_bytes=candidate_bytes, candidate_units=candidate_units,
                percent=round(100 * matched_bytes / total, 6) if total else None,
                evidence=f"decomp/{folder.name}/config/{target['id']}.json")


def report(root):
    editions = []
    for edition in EDITIONS:
        folder = root / edition
        targets = []
        for path in sorted((folder / "config").glob("*.json")):
            target = json.loads(path.read_text(encoding="utf-8"))
            if target["id"] != path.stem:
                raise ValueError(f"target id differs from filename: {path}")
            targets.append(summary(folder, target))
        stage = "Awaiting a reference executable"
        if targets:
            stage = "Matching in progress" if any(t["matched_units"] for t in targets) else "Reference builds identified"
        editions.append(dict(id=edition, stage=stage, targets=targets))
    return dict(schema_version=1, editions=editions)


def verify(target, original):
    data = original.read_bytes()
    if inspect(data) != target["image"]:
        raise ValueError("reference binary differs from the locked build")
    for unit in target["units"]:
        if "match" in unit:
            start, end = span(unit["offset"], unit["size"], len(data))
            if hashlib.sha256(data[start:end]).hexdigest() != unit["match"]["bytes_sha256"]:
                raise ValueError(f"{unit['id']}: receipt differs from retail")
    return data


def match(folder, target, unit_id, retail, rebuilt):
    original = verify(target, retail)
    if retail.samefile(rebuilt):
        raise ValueError("supply the compiler/linker's output as the rebuilt artifact")
    unit = next((u for u in target["units"] if u["id"] == unit_id), None)
    if unit is None:
        raise ValueError(f"unknown unit: {unit_id}")
    output = rebuilt.read_bytes()
    a, b = span(unit["offset"], unit["size"], len(original))
    offset = (coff_symbol_offset(output, unit["rebuilt_symbol"], unit["size"])
              if "rebuilt_symbol" in unit else unit["rebuilt_offset"])
    c, d = span(offset, unit["size"], len(output))
    if original[a:b] != output[c:d]:
        raise ValueError(f"{unit_id}: compiler output differs from retail")
    unit["match"] = dict(bytes_sha256=hashlib.sha256(output[c:d]).hexdigest(),
                         inputs_sha256=inputs_digest(folder, unit, target["image"]["sha256"]))
    summary(folder, target)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    probe = commands.add_parser("identify", help="print an executable's hash and section inventory")
    probe.add_argument("file", type=Path)
    for name in ("verify", "match"):
        cmd = commands.add_parser(name, help="verify the reference baseline" if name == "verify"
                                  else "compare a compiled unit and record its exact match")
        cmd.add_argument("edition", choices=EDITIONS)
        cmd.add_argument("target", type=identity)
        cmd.add_argument("retail", type=Path)
        if name == "match":
            cmd.add_argument("unit", type=identity)
            cmd.add_argument("rebuilt", type=Path)
    generate = commands.add_parser("report", help="regenerate the public progress feed")
    generate.add_argument("--check", action="store_true", help="fail when the committed feed is stale")
    args = parser.parse_args()
    if args.command == "identify":
        print(encoded(inspect(args.file.read_bytes())), end="")
    elif args.command in ("verify", "match"):
        folder = ROOT / args.edition
        path = folder / "config" / (args.target + ".json")
        target = json.loads(path.read_text(encoding="utf-8"))
        if args.command == "verify":
            verify(target, args.retail)
        else:
            match(folder, target, args.unit, args.retail, args.rebuilt)
            write(path, target)
        print(f"Verified {target['label']}")
    else:
        feed = report(ROOT)
        path = ROOT / "reports" / "progress.json"
        if args.check:
            if path.read_text(encoding="utf-8") != encoded(feed):
                raise ValueError("stale report; run the report command")
            print("Progress feed is current.")
        else:
            write(path, feed)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, KeyError, TypeError, OSError, struct.error) as error:
        sys.exit(f"Progress error: {error}")
