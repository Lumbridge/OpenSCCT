"""Imports of the vendored scctpkg and of re/formats/scctformats, with the files they read.

scctformats finds scctpkg by importing it first, so the vendored copy goes on sys.path before scctformats loads.
"""

from __future__ import annotations

import sys
from pathlib import Path

from ..paths import REPO_ROOT, SCCTPKG_ROOT, import_scctpkg

FORMATS_ROOT = REPO_ROOT / "re" / "formats"

# Script packages of the game build (System/_PC_). Editor.u and UnrealEd.u are editor only.
GAME_SCRIPT_PACKAGES = ("Core", "Engine", "SBase", "GUI", "IpDrv", "SoftBody")


def import_formats():
    """The scctformats package from re/formats (after the vendored scctpkg)."""
    import_scctpkg()
    root = str(FORMATS_ROOT)
    if root not in sys.path:
        sys.path.insert(0, root)
    import scctformats  # noqa: PLC0415
    import scctformats.classdefaults  # noqa: F401,PLC0415
    import scctformats.ge  # noqa: F401,PLC0415
    import scctformats.smcollision  # noqa: F401,PLC0415

    where = Path(scctformats.__file__).resolve().parent
    if where != (FORMATS_ROOT / "scctformats").resolve():
        raise ImportError(f"scctformats imported from {where}, expected {FORMATS_ROOT / 'scctformats'}")
    return scctformats


def code_files() -> list[Path]:
    """Source files of scctpkg and scctformats: a change in them changes every converted file."""
    out = sorted(p for p in (SCCTPKG_ROOT / "scctpkg").iterdir() if p.is_file() and p.suffix in (".py", ".json"))
    out += sorted(p for p in (FORMATS_ROOT / "scctformats").iterdir() if p.is_file() and p.suffix == ".py")
    return out


def script_package_files(game: Path) -> list[Path]:
    root = Path(game) / "System" / "_PC_"
    files = [root / f"{n}.u" for n in GAME_SCRIPT_PACKAGES]
    files += text_files(game)
    return [f for f in files if f.is_file()]


def language() -> str:
    """The text language of this build (scctformats.classdefaults.language: "int" English, "fra", ...)."""
    import_formats()
    import scctformats.classdefaults as cd  # noqa: PLC0415

    return cd.language()


def text_files(game: Path) -> list[Path]:
    """System/_PC_ text files the build's language reads (its own and the English fallback)."""
    import_formats()
    import scctformats.classdefaults as cd  # noqa: PLC0415

    return cd.localization_files(Path(game) / "System" / "_PC_")
