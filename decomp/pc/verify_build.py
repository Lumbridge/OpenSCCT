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
from progress import compare_unit, summary, verify


def compiler_identity(cl, env):
    # The Visual C++ Toolkit 2003 predates /Bv. Its banner includes the
    # full tool build and calls the x86 target "80x86".
    banner = subprocess.run([cl], env=env, capture_output=True, text=True, errors="replace")
    legacy = re.search(r"Microsoft .*Compiler Version (13\.\d+\.\d+) for 80x86\b", banner.stderr + banner.stdout)
    if legacy:
        return f"Microsoft C/C++ {legacy[1]} for x86"
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
    parser.add_argument("--cl", action="append", help="x86 cl.exe path; repeat for different toolsets")
    args = parser.parse_args()
    env = os.environ.copy()
    # cl prepends CL and appends _CL_ to the command line.
    env.pop("CL", None)
    env.pop("_CL_", None)
    env["VSLANG"] = "1033"
    compilers = {}
    for requested in args.cl or ["cl.exe"]:
        resolved = shutil.which(requested)
        if resolved is None:
            raise ValueError(f"MSVC not found: {requested}. Pass --cl with the compiler path.")
        cl = str(Path(resolved).resolve())
        build_env = dict(env, PATH=str(Path(cl).parent) + os.pathsep + env.get("PATH", ""))
        identity = compiler_identity(cl, build_env)
        compilers[identity] = (cl, build_env)
    for edition in ("campaign", "versus"):
        target_id = f"steam-252084-{edition}"
        manifest = PC / "config" / f"{target_id}.json"
        target = json.loads(manifest.read_text(encoding="utf-8"))
        if target["id"] != target_id:
            raise ValueError(f"unexpected target ID in {manifest}")
        totals = summary(PC, target)
        retail = args.steam_root / target["file"]
        original = verify(target, retail)
        byte_count = 0
        recipes = {}
        for unit in target["units"]:
            if "match" not in unit:
                raise ValueError(f"{unit['id']}: missing checked-in match receipt")
            if unit["toolchain"] not in compilers:
                raise ValueError(f"{unit['id']}: pass --cl for {unit['toolchain']}")
            key = (unit["toolchain"], unit["build_command"], tuple(sorted(unit["inputs"])))
            recipes.setdefault(key, []).append(unit)
        for (toolchain, _, _), units in recipes.items():
            cl, build_env = compilers[toolchain]
            with tempfile.TemporaryDirectory(prefix="scct-decomp-") as tmp:
                obj = rebuild_unit(units[0], cl, build_env, Path(tmp).resolve()).read_bytes()
            for unit in units:
                if compare_unit(PC, target, unit, original, obj) != unit["match"]:
                    raise ValueError(f"{unit['id']}: rebuilt receipt differs from checked-in receipt")
                byte_count += unit["size"]
                print(f"PASS {target_id}/{unit['id']} at file offset 0x{unit['offset']:x} ({unit['size']} bytes)")
        print(f"{target_id}: {len(target['units'])} functions, {byte_count:,} / {totals['total_bytes']:,} bytes")


if __name__ == "__main__":
    try:
        main()
    except (KeyError, ValueError, OSError, struct.error, subprocess.CalledProcessError) as error:
        sys.exit(f"Verification failed: {error}")
