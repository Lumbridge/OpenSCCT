"""Package a small local TestRange content subset for the browser experiment."""
import argparse
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("--content-root", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--map-folder", type=Path)
parser.add_argument("--map-source", action="append", default=[], metavar="NAME=FOLDER", help="Use an exact published map version instead of the local conversion.")
parser.add_argument("--map", action="append", default=["SteeE", "Entry", "Lobby"], help="Additional map to include (defaults: SteeE and the native menu maps).")
args = parser.parse_args()
root = args.content_root.resolve(strict=True)
groups = ["shared/render", "shared/ui", "shared/fx", "shared/audio",
          "shared/chars/SPerso", "shared/chars/SWeaponFirst", "shared/chars/SGoggle",
          "shared/chars/OpenScctRifles", "shared/chars/OpenScctSidearms"]
if not (root / "svmap/TestRange/manifest.json").is_file():
    parser.error("content root must contain svmap/TestRange/manifest.json")
args.output.parent.mkdir(parents=True, exist_ok=True)
count = total = 0
with zipfile.ZipFile(args.output, "w", zipfile.ZIP_DEFLATED, compresslevel=3) as archive:
    maps = {"TestRange": args.map_folder or root / "svmap/TestRange"}
    for name in args.map:
        folder = root / "svmap" / name
        if not name.isalnum() or not (folder / "manifest.json").is_file():
            parser.error(f"Unknown map: {name}")
        maps[name] = folder
    for override in args.map_source:
        name, separator, folder = override.partition("=")
        source = Path(folder)
        if not separator or name not in maps or not (source / "manifest.json").is_file():
            parser.error(f"Invalid map source: {override}")
        maps[name] = source
    for name, map_folder in maps.items():
        for path in sorted(map_folder.rglob("*")):
            if path.is_file():
                archive.write(path, "svmap/" + name + "/" + path.relative_to(map_folder).as_posix())
                count += 1
                total += path.stat().st_size
    for group in groups:
        for path in sorted((root / group).rglob("*")):
            if path.is_file() and not path.name.startswith("cosmetic_"):
                archive.write(path, path.relative_to(root).as_posix())
                count += 1
                total += path.stat().st_size
print(f"Packaged {count} files, {total:,} bytes -> {args.output} ({args.output.stat().st_size:,} bytes)")
