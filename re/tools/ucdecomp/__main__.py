"""Command line.

    python -B -m ucdecomp check --game <install> [--packages Core,Engine] [--json out.json]
    python -B -m ucdecomp decompile --game <install> [--out <dir>] [--packages ...]
    python -B -m ucdecomp dis --game <install> <Package> <Class.Function | Class.State.Function> ...

Run from re/tools. Outputs default to local/re-out/uc under the repository (gitignored).
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time

from .pkgload import PACKAGES

_REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
DEFAULT_OUT = os.path.join(_REPO, "local", "re-out", "uc")


def _pkgs(arg):
    return tuple(p for p in arg.split(",") if p) if arg else PACKAGES


def cmd_check(args) -> int:
    from .check import check_package
    from .universe import Universe
    t0 = time.time()
    u = Universe.load(args.game)
    report = {"packages": {}, "native_table": len(u.natives), "native_conflicts": len(u.native_conflicts)}
    tot = {}
    for name in _pkgs(args.packages):
        if name not in u.packages:
            print("missing package", name, file=sys.stderr)
            continue
        loadp = u.packages[name]
        r = check_package(u, name)
        r["load_errors"] = len(loadp.errors)
        report["packages"][name] = r
        for k in ("with_script", "T1_ok", "T2_ok", "unstructured_jumps", "with_goto", "calls_checked",
                  "calls_too_many_args", "calls_too_few_args", "load_errors", "native_unknown", "context_checked",
                  "context_skip_mismatch", "skip_checked", "skip_mismatch"):
            tot[k] = tot.get(k, 0) + r.get(k, 0)
        n = r.get("with_script", 0)
        print("%-7s structs %5d  scripts %5d  T1 %5d/%d (%.2f%%)  T2 %5d/%d (%.2f%%)  gotos %d  load errors %d"
              % (name, r.get("structs", 0), n, r.get("T1_ok", 0), n, 100.0 * r.get("T1_ok", 0) / max(n, 1),
                 r.get("T2_ok", 0), n, 100.0 * r.get("T2_ok", 0) / max(n, 1), r.get("unstructured_jumps", 0),
                 r["load_errors"]))
        for k, v in r["fail_samples"].items():
            for path in v[:5]:
                print("    %s fail: %s" % (k, path))
    report["totals"] = tot
    n = tot.get("with_script", 0)
    print("total   scripts %d  T1 %.2f%%  T2 %.2f%%  natives %d  %.1fs"
          % (n, 100.0 * tot.get("T1_ok", 0) / max(n, 1), 100.0 * tot.get("T2_ok", 0) / max(n, 1), len(u.natives),
             time.time() - t0))
    print("        calls checked %d (too many args %d, too few %d)  context skips %d (mismatch %d)"
          "  operand skips %d (mismatch %d)"
          % (tot.get("calls_checked", 0), tot.get("calls_too_many_args", 0), tot.get("calls_too_few_args", 0),
             tot.get("context_checked", 0), tot.get("context_skip_mismatch", 0), tot.get("skip_checked", 0),
             tot.get("skip_mismatch", 0)))
    if args.json:
        with open(args.json, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(report, fh, indent=1)
    ok = tot.get("T1_ok", 0) == n
    return 0 if ok else 1


def cmd_decompile(args) -> int:
    from .emit import decompile_all
    from .universe import Universe
    t0 = time.time()
    u = Universe.load(args.game)
    os.makedirs(args.out, exist_ok=True)
    idx = decompile_all(u, args.out, set(_pkgs(args.packages)))
    for name, st in idx["packages"].items():
        print("%-7s %s" % (name, " ".join("%s=%s" % kv for kv in sorted(st.items()))))
    print("wrote", args.out, "%.1fs" % (time.time() - t0))
    return 0


def cmd_dis(args) -> int:
    from .disasm import listing
    from .universe import Universe
    from .loader import Struct
    u = Universe.load(args.game, names=PACKAGES)
    lp = u.packages[args.package]
    want = set(args.names)
    for f in lp.fields.values():
        if isinstance(f, Struct) and (f.path in want or f.path.split(".", 1)[1] in want):
            print("----", f.path, f.kind, "ScriptSize", f.script_size)
            for line in listing(f.script, lambda n: lp.name_of(n[0]), lp.obj_name):
                print("   ", line)
    return 0


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="ucdecomp")
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("check", help="T1 re-encode and T2 structure round trip on real packages")
    c.add_argument("--game", required=True)
    c.add_argument("--packages", default="")
    c.add_argument("--json", default="")
    d = sub.add_parser("decompile", help="write .uc files and uc-index.json")
    d.add_argument("--game", required=True)
    d.add_argument("--out", default=DEFAULT_OUT)
    d.add_argument("--packages", default="")
    s = sub.add_parser("dis", help="flat token listing of functions or states")
    s.add_argument("--game", required=True)
    s.add_argument("package")
    s.add_argument("names", nargs="+")
    args = ap.parse_args(argv)
    return {"check": cmd_check, "decompile": cmd_decompile, "dis": cmd_dis}[args.cmd](args)


if __name__ == "__main__":
    sys.exit(main())
