"""Package a small local TestRange content subset for the browser experiment."""
import argparse
from pathlib import Path
import zipfile

parser = argparse.ArgumentParser()
parser.add_argument("--content-root", type=Path, required=True)
parser.add_argument("--output", type=Path, required=True)
parser.add_argument("--map-folder", type=Path)
args = parser.parse_args()
root = args.content_root.resolve(strict=True)
groups = ["shared/render", "shared/ui", "shared/fx", "shared/audio",
          "shared/chars/SPerso", "shared/chars/SWeaponFirst", "shared/chars/SGoggle"]
if not (root / "svmap/TestRange/manifest.json").is_file():
    parser.error("content root must contain svmap/TestRange/manifest.json")
args.output.parent.mkdir(parents=True, exist_ok=True)
count = total = 0
with zipfile.ZipFile(args.output, "w", zipfile.ZIP_DEFLATED, compresslevel=3) as archive:
    map_folder = args.map_folder or root / "svmap/TestRange"
    for path in sorted(map_folder.rglob("*")):
        if path.is_file():
            archive.write(path, "svmap/TestRange/" + path.relative_to(map_folder).as_posix())
            count += 1
            total += path.stat().st_size
    for group in groups:
        for path in sorted((root / group).rglob("*")):
            if path.is_file() and not path.name.startswith("cosmetic_"):
                archive.write(path, path.relative_to(root).as_posix())
                count += 1
                total += path.stat().st_size
print(f"Packaged {count} files, {total:,} bytes -> {args.output} ({args.output.stat().st_size:,} bytes)")
