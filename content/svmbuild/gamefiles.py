"""The game's files: the player's install, plus the downloaded maps folder (addons) when there is one.

The launcher downloads maps from the SCCT-Maps repository into its own folder laid out like an install
(Packages/Maps, Packages/Textures, ..., System/_PC_) and passes it as --addons (SVM_ADDONS for the worker
processes). The install always wins: an addon file is only used when the install has no file of that name, so a
map that ships its own copy of a stock package (SWeapon.ukx, AQU_anm.ukx) never changes the stock content.
Logical paths of addon files use the "addons:" scheme (graph.Context.resolve).
"""

from __future__ import annotations

import os
from pathlib import Path

ADDONS_ENV = "SVM_ADDONS"


def addons_root() -> Path | None:
    """The downloaded maps folder of this build, or None."""
    value = os.environ.get(ADDONS_ENV, "").strip()
    if not value:
        return None
    root = Path(value)
    return root if root.is_dir() else None


def roots(game: Path) -> list[Path]:
    """The folders laid out like an install, in lookup order: the install first, then the addons."""
    out = [Path(game)]
    extra = addons_root()
    if extra is not None:
        out.append(extra)
    return out


def package_roots(game: Path, *more: str) -> list[Path]:
    """Roots for scctpkg's PackageSet: each root's Packages (and any `more` subfolders such as System).

    PackageSet lets the last root win for a package name (maps excepted), so the addons come first and the install
    last: the install's package wins when both have one."""
    out: list[Path] = []
    for root in reversed(roots(game)):
        for sub in ("Packages", *more):
            if (root / sub).is_dir():
                out.append(root / sub)
    return out


def game_file(game: Path, rel: str) -> Path:
    """<install>/rel, or the addons' file when only the addons have it (the install's path when neither does)."""
    first = Path(game) / rel
    if first.exists():
        return first
    extra = addons_root()
    if extra is not None and (extra / rel).exists():
        return extra / rel
    return first


def files(game: Path, sub: str, pattern: str = "*") -> list[Path]:
    """The files matching pattern in <root>/<sub> of every root, one per file name (ignoring case, the install's
    first), sorted by name ignoring case."""
    seen: dict[str, Path] = {}
    for root in roots(game):
        folder = root / sub
        if not folder.is_dir():
            continue
        for p in folder.glob(pattern):
            if p.is_file():
                seen.setdefault(p.name.lower(), p)
    return [seen[k] for k in sorted(seen)]


def find_file(game: Path, sub: str, name: str) -> Path:
    """<root>/<sub>/<name> matched ignoring case, the install first (the install's path when there is none)."""
    want = name.lower()
    for root in roots(game):
        folder = root / sub
        exact = folder / name
        if exact.is_file():
            return exact
        if folder.is_dir():
            for p in folder.iterdir():
                if p.name.lower() == want and p.is_file():
                    return p
    return Path(game) / sub / name


def text_dir(game: Path, package: str) -> Path:
    """The System/_PC_ folder that has <package>'s text file: the install's, else the addons' (else the install's)."""
    from .sources.env import import_formats  # noqa: PLC0415

    import_formats()
    import scctformats.classdefaults as cd  # noqa: PLC0415

    for root in roots(game):
        folder = root / "System" / "_PC_"
        if folder.is_dir() and cd.localization_file(folder, package) is not None:
            return folder
    return Path(game) / "System" / "_PC_"


def addon_text_files(map_name: str) -> list[Path]:
    """The addons' System/_PC_ text files of one map that the build's language reads (<Map> and <Map>-i: names,
    briefings, zone tips of a downloaded map)."""
    extra = addons_root()
    if extra is None:
        return []
    from .sources.env import import_formats  # noqa: PLC0415

    import_formats()
    import scctformats.classdefaults as cd  # noqa: PLC0415

    wanted = {map_name.lower(), map_name.lower() + "-i"}
    return [p for p in cd.localization_files(extra / "System" / "_PC_") if p.stem.lower() in wanted]
