"""Command line: python -m svmbuild <step|all> [...] [--maps SteeE] [--game PATH] [--out PATH]

Run from the content/ folder, or with content/ on PYTHONPATH:

    python -m svmbuild maps-preview --maps SteeE
    python -m svmbuild testbox
    python -m svmbuild all --maps SteeE,FactD
    python -m svmbuild maps validate --maps all --jobs 8 --report .content/reports/maps.json
    python -m svmbuild --list

--game defaults to $SVM_GAME_ROOT, else the reference install; --out to $SVM_CONTENT, else <repo>/.content.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path

from .graph import Cache, Context, Graph
from .hashing import FileHasher
from .paths import SCCTPKG_ROOT, default_game_root, default_out_root, is_within
from .sources import classdefaults_dump
from .steps import all_steps


def _resolve_maps(game: Path, spec: str | None) -> list[str]:
    maps_dir = game / "Packages" / "Maps"
    if not maps_dir.is_dir():
        raise SystemExit(f"no Packages/Maps folder under {game} (set --game or SVM_GAME_ROOT)")
    available = {p.stem.lower(): p.stem for p in maps_dir.glob("*.sdc")}
    if spec is None or spec.strip().lower() == "all":
        return sorted(available.values(), key=str.lower)
    out = []
    for name in (n.strip() for n in spec.split(",")):
        if not name:
            continue
        if name.lower() not in available:
            raise SystemExit(f"unknown map {name!r}: no {name}.sdc in {maps_dir}")
        if available[name.lower()] not in out:
            out.append(available[name.lower()])
    return out


def main(argv: list[str] | None = None) -> int:
    graph = Graph(all_steps())
    ap = argparse.ArgumentParser(prog="python -m svmbuild", description="SVM content pipeline.",
                                 formatter_class=argparse.RawDescriptionHelpFormatter,
                                 epilog="steps: " + ", ".join(graph.steps) + ", or all")
    ap.add_argument("targets", nargs="*", help="steps to build (with their dependencies), or all")
    ap.add_argument("--maps", help="comma list of map names, or all (default: all maps)")
    ap.add_argument("--game", type=Path, default=None,
                    help="game root (default $SVM_GAME_ROOT or the reference install)")
    ap.add_argument("--out", type=Path, default=None, help="content root (default $SVM_CONTENT or <repo>/.content)")
    ap.add_argument("--force", action="store_true", help="rebuild even when the cache has the result")
    ap.add_argument("--no-cache", action="store_true", help="neither read nor write the cache")
    ap.add_argument("--no-deps", action="store_true", help="run only the named steps, not their dependencies")
    ap.add_argument("--rehash", action="store_true", help="re-read every file instead of trusting size and mtime")
    ap.add_argument("--list", action="store_true", help="list the steps and exit")
    ap.add_argument("--jobs", type=int, default=1, help="worker processes for per-map steps (default 1)")
    ap.add_argument("--check-stable", action="store_true",
                    help="validate: convert each map a second time and require byte-identical files")
    ap.add_argument("--sim-only", action="store_true", help="maps: write sim/ and ui/ only, no render/")
    ap.add_argument("--language", default=None,
                    help="text language: int (English, default), fra, deu, esp or ita; $SVM_LANGUAGE otherwise. "
                         "Text an install has no file for in that language stays English")
    ap.add_argument("--report", type=Path, default=None, help="write every unit result (status, time, stats) as JSON")
    args = ap.parse_args(argv)

    if args.list or not args.targets:
        for s in graph.steps.values():
            kind = "per map" if s.per_map else "global"
            state = "stub" if s.stub else "ready"
            deps = ", ".join(s.deps) or "-"
            print(f"{s.name:13s} {kind:8s} {state:6s} deps: {deps:28s} {s.description}")
        return 0 if args.list else 2

    targets = list(graph.steps) if "all" in args.targets else args.targets
    unknown = [t for t in targets if t not in graph.steps]
    if unknown:
        print(f"unknown step(s): {', '.join(unknown)}; try --list", file=sys.stderr)
        return 2
    plan = graph.order(targets, with_deps=not args.no_deps)

    game = (args.game or default_game_root()).resolve()
    out = (args.out or default_out_root()).resolve()
    if is_within(out, game) or is_within(game, out):
        print(f"--out {out} must not overlap the game folder {game}", file=sys.stderr)
        return 2
    needs_maps = any(s.per_map for s in plan)
    if any(s.needs_game and not s.stub for s in plan) and not (game / "Packages").is_dir():
        print(f"no Packages folder under {game} (set --game or SVM_GAME_ROOT)", file=sys.stderr)
        return 2
    maps = _resolve_maps(game, args.maps) if needs_maps else []

    out.mkdir(parents=True, exist_ok=True)
    # Worker processes and scctpkg's class defaults find the content root through SVM_CONTENT; an installed builder
    # has no class_defaults.json, so it is made from the classes already built (before any cache key reads it).
    os.environ["SVM_CONTENT"] = str(out)
    # The text language (scctformats.classdefaults.language) reaches the worker processes the same way.
    language = (args.language or os.environ.get("SVM_LANGUAGE") or "int").strip().lower()
    from .sources.env import import_formats  # noqa: PLC0415

    import_formats()
    import scctformats.classdefaults as cd  # noqa: PLC0415

    if language not in cd.LANGUAGES:
        print(f"unknown --language {language!r}: one of {', '.join(cd.LANGUAGES)}", file=sys.stderr)
        return 2
    os.environ["SVM_LANGUAGE"] = language
    classdefaults_dump.ensure(SCCTPKG_ROOT / "scctpkg", out)
    cache_root = out / "cache"
    hasher = FileHasher(cache_root / "filehash.json", trust_stat=not args.rehash)
    cache = None if args.no_cache else Cache(cache_root)
    options = {"check_stable": args.check_stable, "sim_only": args.sim_only}
    ctx = Context(game=game, out=out, hasher=hasher, cache=cache, maps=maps, force=args.force, options=options,
                  jobs=max(1, args.jobs))
    print(f"game {game}\nout  {out}\nlang {language} ({cd.LANGUAGES[language]})\nplan {' '.join(s.name for s in plan)}" + (f"  maps {len(maps)}" if maps else "")
          + (f"  jobs {ctx.jobs}" if ctx.jobs > 1 else ""))
    wall = time.perf_counter()
    results = graph.run(ctx, targets, with_deps=not args.no_deps)
    wall = time.perf_counter() - wall
    if args.report is not None:
        report = {"game": str(game), "out": str(out), "jobs": ctx.jobs, "wallSeconds": round(wall, 2),
                  "results": [{"step": r.step, "unit": r.unit, "status": r.status, "seconds": round(r.seconds, 3),
                               "key": r.key, "stats": r.stats, "error": r.error} for r in results]}
        args.report.parent.mkdir(parents=True, exist_ok=True)
        args.report.write_text(json.dumps(report, indent=1, sort_keys=True), encoding="utf-8")
    counts: dict[str, int] = {}
    for r in results:
        counts[r.status] = counts.get(r.status, 0) + 1
    total = sum(r.seconds for r in results)
    print("done: " + ", ".join(f"{v} {k}" for k, v in sorted(counts.items())) + f" in {total:.2f}s"
          + (f" ({wall:.2f}s wall)" if ctx.jobs > 1 else ""))
    return 0 if all(r.ok for r in results) else 1
