"""Package the exact dynamically linked LGPL source, including our callback adapter."""
from pathlib import Path
import sys
import zipfile

source, patch, destination = map(Path, sys.argv[1:4])
destination.parent.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(destination, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for item in sorted(source.rglob("*")):
        relative = item.relative_to(source)
        if item.is_file() and not any(part in {".git", "__pycache__"} for part in relative.parts):
            archive.write(item, "openal-soft-1.24.3/" + relative.as_posix())
    archive.write(patch, "patch_openal_callback.py")
