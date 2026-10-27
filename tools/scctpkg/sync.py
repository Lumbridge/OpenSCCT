"""Re-copy the vendored scctpkg sources from a Bridge checkout.

    python tools/scctpkg/sync.py [--bridge PATH] [--ref REV] [--check]

Files are read from the Bridge git object store at REV (default HEAD), not from
its working tree, so the vendored copy is byte-identical to the upstream blobs
whatever the checkout's line-ending settings are. UPSTREAM.md records the
commit and every blob id.

class_defaults.json is copied too because scctpkg loads it at import time, but
it is game-derived and stays untracked (see tools/scctpkg/.gitignore).

--check verifies the vendored files against the blob ids listed in UPSTREAM.md
without touching anything (exit code 1 on mismatch).

Local patches: a fix that has not reached upstream yet lives in patches/NNNN-name.patch (paths relative to
scctpkg/). sync applies every patch after copying, in name order; --check reverses the patches on a temporary
copy and verifies the result against the upstream blob ids, so the vendored files are always exactly
upstream plus the listed patches. Send each patch upstream and delete it once the Bridge has it.
"""

from __future__ import annotations

import argparse
import datetime
import hashlib
import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
DEST = HERE / "scctpkg"
UPSTREAM_MD = HERE / "UPSTREAM.md"
PATCHES = HERE / "patches"
DEFAULT_BRIDGE = Path(os.environ.get("SVM_BRIDGE_ROOT", os.path.expanduser(r"~\source\repos\SCCT_Versus_UE5_Bridge")))
UPSTREAM_DIR = "scctpkg"
# Copied for imports to work, never tracked here.
LOCAL_ONLY = {"class_defaults.json"}
# Vendored and tracked: every .py plus these data files.
TRACKED_DATA = {"array_inner_types.json"}


def git(bridge: Path, *args: str) -> bytes:
    return subprocess.run(["git", "-C", str(bridge), *args], check=True, capture_output=True).stdout


def blob_id(data: bytes) -> str:
    return hashlib.sha1(b"blob %d\0" % len(data) + data).hexdigest()


def wanted(name: str) -> bool:
    return name.endswith(".py") or name in TRACKED_DATA or name in LOCAL_ONLY


def list_upstream(bridge: Path, ref: str) -> list[tuple[str, str]]:
    out = git(bridge, "ls-tree", ref, f"{UPSTREAM_DIR}/").decode("utf-8")
    files = []
    for line in out.splitlines():
        meta, path = line.split("\t", 1)
        _mode, kind, oid = meta.split()
        name = path.rsplit("/", 1)[-1]
        if kind == "blob" and wanted(name):
            files.append((name, oid))
    return sorted(files)


def write_upstream_md(bridge: Path, commit: str, commit_date: str, remote: str, files: list[tuple[str, str]]) -> None:
    today = datetime.date.today().isoformat()
    lines = [
        "# Vendored scctpkg",
        "",
        "Python reader for SCCT Versus packages, copied from the Bridge repository.",
        "Do not edit files under `scctpkg/` here: change them upstream and re-run `sync.py`. A fix that cannot wait",
        "goes in `patches/` (applied by `sync.py`, verified by `sync.py --check`) until upstream has it.",
        "",
        f"- Source repository: {remote}",
        f"- Source path: `{UPSTREAM_DIR}/` (local checkout `{bridge}`)",
        f"- Commit: `{commit}`",
        f"- Commit date: {commit_date}",
        f"- Vendored on: {today}",
        "",
        "Files are taken from the git object store at that commit, so each one matches its upstream blob id.",
        "`class_defaults.json` is game-derived: it is copied for imports to work but is not tracked",
        "(`tools/scctpkg/.gitignore`). Re-run `python tools/scctpkg/sync.py` on a fresh clone to get it.",
        "",
        "A later split of scctpkg into its own repository (consumed here as a git subtree) is planned.",
        "",
        "## Files",
        "",
        "| File | Blob | Tracked |",
        "|---|---|---|",
    ]
    for name, oid in files:
        lines.append(f"| `{name}` | `{oid}` | {'no' if name in LOCAL_ONLY else 'yes'} |")
    lines.append("")
    UPSTREAM_MD.write_text("\n".join(lines), encoding="utf-8", newline="\n")


def patch_files() -> list[Path]:
    return sorted(PATCHES.glob("*.patch")) if PATCHES.is_dir() else []


def patched_names(patches: list[Path]) -> set[str]:
    names: set[str] = set()
    for patch in patches:
        for line in patch.read_text(encoding="utf-8").splitlines():
            if line.startswith("+++ b/"):
                names.add(line[len("+++ b/"):].strip())
    return names


def apply_patches(folder: Path, patches: list[Path], reverse: bool = False) -> None:
    """Apply (or reverse) the patches to the files in `folder`, a directory outside any git repository."""
    for patch in (reversed(patches) if reverse else patches):
        # core.autocrlf off: a global autocrlf=true makes git apply write CRLF, which breaks the blob ids.
        args = ["git", "-c", "core.autocrlf=false", "apply", "--whitespace=nowarn"] + (["--reverse"] if reverse else []) + [str(patch)]
        result = subprocess.run(args, cwd=folder, capture_output=True, text=True)
        if result.returncode != 0:
            raise RuntimeError(f"{'reversing' if reverse else 'applying'} {patch.name} failed: {result.stderr.strip()}")


def read_recorded() -> dict[str, str]:
    if not UPSTREAM_MD.exists():
        return {}
    rows = re.findall(r"^\| `([^`]+)` \| `([0-9a-f]{40})` \|", UPSTREAM_MD.read_text(encoding="utf-8"), re.M)
    return dict(rows)


def check() -> int:
    recorded = read_recorded()
    if not recorded:
        print("UPSTREAM.md has no file table", file=sys.stderr)
        return 1
    bad = 0
    patches = patch_files()
    patched = patched_names(patches)
    upstream_bytes: dict[str, bytes] = {}
    if patched:
        with tempfile.TemporaryDirectory() as tmp:
            for name in patched:
                if (DEST / name).exists():
                    shutil.copyfile(DEST / name, Path(tmp) / name)
            try:
                apply_patches(Path(tmp), patches, reverse=True)
            except RuntimeError as exc:
                print(exc)
                return 1
            upstream_bytes = {n: (Path(tmp) / n).read_bytes() for n in patched if (Path(tmp) / n).exists()}
    for name, oid in sorted(recorded.items()):
        p = DEST / name
        if not p.exists() and name in LOCAL_ONLY:
            print(f"skipped {name} (local only, not present)")
            continue
        if not p.exists():
            print(f"missing {name}")
            bad += 1
        elif blob_id(upstream_bytes.get(name, p.read_bytes()) if name in patched else p.read_bytes()) != oid:
            print(f"changed {name}" + (" (beyond its patches)" if name in patched else ""))
            bad += 1
    for patch in patches:
        print(f"patch {patch.name}")
    extra = sorted(p.name for p in DEST.iterdir() if p.is_file() and wanted(p.name) and p.name not in recorded)
    for name in extra:
        print(f"unrecorded {name}")
        bad += 1
    print("ok" if not bad else f"{bad} problem(s)")
    return 1 if bad else 0


def sync(bridge: Path, ref: str) -> int:
    commit = git(bridge, "rev-parse", f"{ref}^{{commit}}").decode().strip()
    commit_date = git(bridge, "log", "-1", "--format=%cI", commit).decode().strip()
    try:
        remote = git(bridge, "remote", "get-url", "origin").decode().strip()
    except subprocess.CalledProcessError:
        remote = str(bridge)
    files = list_upstream(bridge, commit)
    if not files:
        print(f"no {UPSTREAM_DIR}/ files at {commit}", file=sys.stderr)
        return 2
    DEST.mkdir(parents=True, exist_ok=True)
    names = {n for n, _ in files}
    for p in DEST.iterdir():
        if p.is_file() and wanted(p.name) and p.name not in names:
            p.unlink()
            print(f"removed {p.name}")
    for name, oid in files:
        data = git(bridge, "cat-file", "blob", oid)
        if blob_id(data) != oid:
            raise RuntimeError(f"blob id mismatch for {name}")
        target = DEST / name
        if not target.exists() or target.read_bytes() != data:
            target.write_bytes(data)
            print(f"wrote {name}")
    patches = patch_files()
    if patches:
        with tempfile.TemporaryDirectory() as tmp:
            for name in patched_names(patches):
                shutil.copyfile(DEST / name, Path(tmp) / name)
            apply_patches(Path(tmp), patches)
            for name in patched_names(patches):
                shutil.copyfile(Path(tmp) / name, DEST / name)
                print(f"patched {name}")
    write_upstream_md(bridge, commit, commit_date, remote, files)
    print(f"synced {len(files)} files from {commit}" + (f" with {len(patches)} local patch(es)" if patches else ""))
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--bridge", type=Path, default=DEFAULT_BRIDGE, help="Bridge checkout (default %(default)s)")
    ap.add_argument("--ref", default="HEAD", help="git revision to copy from (default HEAD)")
    ap.add_argument("--check", action="store_true", help="verify vendored files against UPSTREAM.md")
    args = ap.parse_args(argv)
    if args.check:
        return check()
    return sync(args.bridge, args.ref)


if __name__ == "__main__":
    raise SystemExit(main())
