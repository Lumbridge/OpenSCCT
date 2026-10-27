"""Command line: python -m scctpkg export <game dir> <MapName|all> [--out DIR]

    python -m scctpkg export "C:\\Games\\SCCT Versus" FactD --out out
    python -m scctpkg export "C:\\Games\\SCCT Versus" all
    python -m scctpkg list  "C:\\Games\\SCCT Versus\\Packages\\Maps\\FactD.sdc"
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

from .export_gltf import export_map, export_package, export_textures
from .package import Package
from .packageset import PackageSet


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="scctpkg")
    sub = ap.add_subparsers(dest="cmd", required=True)
    ex = sub.add_parser("export", help="export map(s) to glTF + scene.json")
    ex.add_argument("game", help="game root containing Packages\\")
    ex.add_argument("map", help="map name (without .sdc) or 'all'")
    ex.add_argument("--out", default="out")
    ex.add_argument("--no-bsp", action="store_true")
    ep = sub.add_parser("export-package", help="export every static mesh of .usx package(s) to out/_shared")
    ep.add_argument("game", help="game root containing Packages\\")
    ep.add_argument("package", help="package name (without .usx) or comma list")
    ep.add_argument("--out", default="out/_shared")
    et = sub.add_parser("export-textures", help="export textures (or materials, resolved to their texture) to PNG")
    et.add_argument("game", help="game root containing Packages\\")
    et.add_argument("names", help="comma list of Package.Name, e.g. SGoggleTextures.TRD_goggle_motion")
    et.add_argument("--out", default="out/_textures")
    ls = sub.add_parser("list", help="list exports of a package")
    ls.add_argument("file")
    args = ap.parse_args(argv)

    if args.cmd == "list":
        pkg = Package.load(args.file)
        print(f"{pkg.path.name}: version {pkg.version}, {len(pkg.names)} names, "
              f"{len(pkg.imports)} imports, {len(pkg.exports)} exports")
        for e in pkg.exports:
            print(f"  {e.index + 1:6d} {e.class_name:24s} {e.name:40s} {e.size:9d} @ {e.offset}")
        return 0

    game = Path(args.game)
    pkg_root = game / "Packages"
    if not pkg_root.is_dir():
        print(f"no Packages folder under {game}", file=sys.stderr)
        return 2
    pset = PackageSet([pkg_root])
    if args.cmd == "export-textures":
        written = export_textures([n for n in args.names.split(",") if n], pset, Path(args.out))
        for w in written:
            print(w)
        print(f"{len(written)} textures")
        return 0
    if args.cmd == "export-package":
        for name in args.package.split(","):
            usx = pkg_root / "StaticMeshes" / f"{name}.usx"
            if not usx.exists():
                print(f"missing {usx}", file=sys.stderr)
                continue
            t = time.time()
            st = export_package(usx, pset, Path(args.out))
            print(f"{name}: {st['meshes']} meshes, {len(st['failed'])} failed ({time.time() - t:.1f}s)")
        return 0
    out_root = Path(args.out)
    maps = sorted((pkg_root / "Maps").glob("*.sdc")) if args.map.lower() == "all" else [pkg_root / "Maps" / f"{args.map}.sdc"]
    for m in maps:
        if not m.exists():
            print(f"missing {m}", file=sys.stderr)
            continue
        t = time.time()
        stats = export_map(m, pset, out_root, include_bsp=not args.no_bsp)
        print(f"{m.stem}: bsp polys {stats['bsp_polys']}, static meshes {stats['static_meshes']}, "
              f"actors {stats['actors']}, missing {len(stats['missing_meshes'])}  ({time.time() - t:.1f}s)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
