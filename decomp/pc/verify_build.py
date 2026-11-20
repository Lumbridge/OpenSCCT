"""Rebuild the Steam 252084 PC units and verify their checked-in byte receipts."""

import argparse
import json
import os
from pathlib import Path
import re
import shlex
import shutil
import struct
import subprocess
import sys
import tempfile

PC = Path(__file__).resolve().parent
sys.path.insert(0, str(PC.parent / "tools"))
from progress import match, summary, verify


def compiler_identity(cl, env):
    result = subprocess.run([cl, "/Bv", "/?"], env=env, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True, errors="replace", check=True)
    version = re.search(r"(?im)[\\/]cl\.exe:\s*Version (\d+\.\d+\.\d+\.\d+)", result.stdout)
    arch = re.search(r"Microsoft .*C/C\+\+ Optimizing Compiler Version [\d.]+ for (\w+)", result.stdout)
    if not version or not arch:
        raise ValueError(f"could not identify Microsoft C/C++ compiler at {cl}")
    return f"Microsoft C/C++ {version[1]} for {arch[1]}"


def rebuild_unit(unit, cl, env, build_dir):
    # Recipes use relative forward-slash paths, even on Windows.
    program, *args = shlex.split(unit["build_command"])
    if program.lower() not in ("cl", "cl.exe") or "/c" not in args or "rebuilt_symbol" not in unit:
        raise ValueError("expected a compile-only cl recipe for a named x86 COFF function")
    outputs = [arg[3:] or args[i + 1] for i, arg in enumerate(args) if arg.startswith("/Fo")]
    if len(outputs) != 1:
        raise ValueError("expected one /Fo object output in the build command")
    obj = (build_dir / outputs[0]).resolve()
    if not obj.is_relative_to(build_dir / "local") or obj.suffix.lower() != ".obj":
        raise ValueError("expected a local/*.obj output")
    for relative in unit["inputs"]:
        destination = build_dir / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(PC / relative, destination)
    obj.parent.mkdir(parents=True, exist_ok=True)
    if obj.exists():
        raise ValueError("the expected object already exists in the fresh build directory")
    subprocess.run([cl, *args], cwd=build_dir, env=env, check=True)
    if not obj.is_file():
        raise ValueError(f"compiler produced no object at {outputs[0]}")
    return obj


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("steam_root", type=Path, help="installed Splinter Cell Chaos Theory folder")
    parser.add_argument("--cl", default="cl.exe", help="path to the manifest's x86 MSVC cl.exe")
    args = parser.parse_args()
    cl = shutil.which(args.cl)
    if cl is None:
        raise ValueError(f"MSVC not found: {args.cl}. Use --cl or run in an x86 Developer PowerShell.")
    cl = str(Path(cl).resolve())
    env = os.environ.copy()
    # cl prepends CL and appends _CL_ to the command line.
    env.pop("CL", None)
    env.pop("_CL_", None)
    env["VSLANG"] = "1033"
    toolchain = compiler_identity(cl, env)
    for edition in ("campaign", "versus"):
        target_id = f"steam-252084-{edition}"
        manifest = PC / "config" / f"{target_id}.json"
        target = json.loads(manifest.read_text(encoding="utf-8"))
        if target["id"] != target_id:
            raise ValueError(f"unexpected target ID in {manifest}")
        totals = summary(PC, target)
        retail = args.steam_root / target["file"]
        verify(target, retail)
        byte_count = 0
        for unit in target["units"]:
            receipt = unit.get("match")
            if receipt is None:
                raise ValueError(f"{unit['id']}: missing checked-in match receipt")
            if unit["toolchain"] != toolchain:
                raise ValueError(f"{unit['id']}: expected {unit['toolchain']}, found {toolchain}")
            with tempfile.TemporaryDirectory(prefix="scct-decomp-") as tmp:
                obj = rebuild_unit(unit, cl, env, Path(tmp).resolve())
                match(PC, target, unit["id"], retail, obj)
            if unit["match"] != receipt:
                raise ValueError(f"{unit['id']}: rebuilt receipt differs from checked-in receipt")
            byte_count += unit["size"]
            print(f"PASS {target_id}/{unit['id']} at file offset 0x{unit['offset']:x} ({unit['size']} bytes)")
        print(f"{target_id}: {len(target['units'])} functions, {byte_count:,} / {totals['total_bytes']:,} bytes")


if __name__ == "__main__":
    try:
        main()
    except (KeyError, ValueError, OSError, struct.error, subprocess.CalledProcessError) as error:
        sys.exit(f"Verification failed: {error}")
