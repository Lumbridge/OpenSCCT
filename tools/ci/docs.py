#!/usr/bin/env python3
"""Check local Markdown links and the README's porting metrics; --write refreshes the metrics.

Only tracked and non-ignored new files are checked. External URLs are not fetched.
Supports inline links/images, reference definitions, HTML href/src and ATX headings.
Fenced code, indented code and inline code are excluded from link checks.
"""

from __future__ import annotations

import argparse
import csv
import html
import json
import posixpath
import re
import subprocess
import sys
import unicodedata
from pathlib import Path
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[2]
START = "<!-- coverage:start -->"
END = "<!-- coverage:end -->"


def prose_lines(text: str):
    fence = ""
    for number, line in enumerate(text.splitlines(), 1):
        match = re.match(r"^ {0,3}(`{3,}|~{3,})", line)
        if match:
            marker = match[1]
            if not fence:
                fence = marker
            elif marker[0] == fence[0] and len(marker) >= len(fence):
                fence = ""
            continue
        if not fence and not line.startswith(("    ", "\t")):
            yield number, line


def anchors(text: str) -> set[str]:
    found: set[str] = set()
    for _, line in prose_lines(text):
        found.update(re.findall(r'<[^>]+\b(?:id|name)=["\']([^"\']+)["\']', line))
        match = re.match(r"^ {0,3}#{1,6}\s+(.+?)\s*#*\s*$", line)
        if not match:
            continue
        title = re.sub(r"!?\[([^\]]+)\]\([^)]*\)", r"\1", match[1])
        title = html.unescape(re.sub(r"<[^>]+>", "", title)).lower()
        slug = "".join(c for c in title if c in " -_" or unicodedata.category(c)[0] in "LNM")
        slug = slug.replace(" ", "-")
        unique, suffix = slug, 0
        while unique in found:
            suffix += 1
            unique = f"{slug}-{suffix}"
        found.add(unique)
    return found


def links(text: str):
    for number, line in prose_lines(text):
        line = re.sub(r"(`+).*?\1", "", line)
        # One nested parenthesis level handles ordinary filenames and heading fragments.
        for match in re.finditer(r"\]\(\s*(<[^>]+>|[^\s()]+(?:\([^()]*\)[^\s()]*)*)", line):
            yield number, match[1].strip("<>")
        match = re.match(r"^ {0,3}\[[^\]]+\]:\s*(<[^>]+>|\S+)", line)
        if match:
            yield number, match[1].strip("<>")
        for match in re.finditer(r'<[^>]+\b(?:href|src)=["\']([^"\']+)["\']', line):
            yield number, match[1]


def check_links(root: Path, paths: set[str]) -> list[str]:
    errors: list[str] = []
    dirs = {"."}
    for path in paths:
        parent = posixpath.dirname(path)
        while parent:
            dirs.add(parent)
            parent = posixpath.dirname(parent)
    headings: dict[str, set[str]] = {}
    for path in sorted(p for p in paths if p.endswith(".md")):
        text = (root / path).read_text(encoding="utf-8-sig")
        for number, target in links(text):
            url = urlsplit(html.unescape(target))
            if url.scheme or url.netloc:
                continue
            destination = unquote(url.path)
            if destination.startswith("/"):
                resolved = posixpath.normpath(destination.lstrip("/"))
            elif destination:
                resolved = posixpath.normpath(posixpath.join(posixpath.dirname(path), destination))
            else:
                resolved = path
            if resolved not in paths and resolved not in dirs:
                errors.append(f"{path}:{number}: missing or wrong-case target: {target}")
                continue
            if url.fragment and resolved.endswith(".md"):
                if resolved not in headings:
                    headings[resolved] = anchors((root / resolved).read_text(encoding="utf-8-sig"))
                if unquote(url.fragment) not in headings[resolved]:
                    errors.append(f"{path}:{number}: missing heading: {target}")
    return errors


def coverage_block(report: dict, history: list[dict]) -> str:
    total = next(row for row in report["summary"] if row["key"] == "all")
    gameplay = next(row for row in report["packages"] if row["key"] == "SBase")
    scope_bytes = total["bytes"] - total["naBytes"]
    weights = {f"{key}_w": f'{100 * total[f"{key}Bytes"] / scope_bytes:.2f}' if scope_bytes else "0.00"
               for key in ("specced", "ported", "verified")}
    matches = [row for row in history if all(int(row[key]) == value for key, value in {
        "units": total["units"], "na_units": total["na"],
        "in_scope_bytes": total["bytes"] - total["naBytes"],
        "specced_units": total["specced"], "ported_units": total["ported"],
        "verified_units": total["verified"],
    }.items()) and all(f'{float(row[key]):.2f}' == value for key, value in weights.items())]
    date = matches[-1]["date"] if matches else "date unavailable (no matching history row)"
    lines = [START, f"Snapshot: **{date}**. Generated from the checked-in coverage report.", "",
             "| Scope | In-scope units | Specced | Ported | Verified | Ported bytecode |",
             "|---|---:|---:|---:|---:|---:|"]
    for label, row in [("All inventoried units", total), ("SBase gameplay package", gameplay)]:
        count = row["units"] - row["na"]
        size = row["bytes"] - row["naBytes"]
        values = [f'{row[key]:,} ({100 * row[key] / count:.1f}%)' if count else "n/a"
                  for key in ("specced", "ported", "verified")]
        weight = f'{100 * row["portedBytes"] / size:.1f}%' if size else "n/a"
        lines.append(f'| {label} | {count:,} | {" | ".join(values)} | {weight} |')
    lines.extend(["", "Bytecode percentages weight each unit by its original script size; units without",
                  "bytecode count only in the unit columns.", END])
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--write", action="store_true", help="refresh only the README coverage block, then check links")
    args = parser.parse_args()
    readme = ROOT / "README.md"
    current = readme.read_text(encoding="utf-8-sig")
    report = json.loads((ROOT / "docs/progress/coverage.json").read_text(encoding="utf-8-sig"))
    with (ROOT / "docs/progress/history.csv").open(encoding="utf-8-sig", newline="") as handle:
        expected = coverage_block(report, list(csv.DictReader(handle)))
    if current.count(START) != 1 or current.count(END) != 1 or current.index(START) > current.index(END):
        print("README.md: expected one ordered pair of coverage markers", file=sys.stderr)
        return 1
    updated = current[:current.index(START)] + expected + current[current.index(END) + len(END):]
    errors = []
    if current != updated:
        if args.write:
            readme.write_text(updated, encoding="utf-8", newline="\n")
            print("Updated README porting metrics.")
        else:
            errors.append("README.md: coverage table is stale; run python tools/ci/docs.py --write")
    result = subprocess.run(["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"],
                            cwd=ROOT, check=True, capture_output=True)
    paths = {path for path in result.stdout.decode("utf-8").split("\0") if path and (ROOT / path).is_file()}
    errors.extend(check_links(ROOT, paths))
    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    count = sum(path.endswith(".md") for path in paths)
    print(f"Documentation OK: {count} Markdown files; README metrics match the checked-in report.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
