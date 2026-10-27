"""Locate and import the scctpkg package reader.

Search order: the copy vendored in this repository (tools/scctpkg), then the Bridge checkout, then
whatever is already importable. The reader handles the v300 container, the XOR-obfuscated name table
and the import and export tables; this tool builds on its Package and Reader classes.
"""

from __future__ import annotations

import os
import sys

_HERE = os.path.dirname(os.path.abspath(__file__))
_REPO = os.path.normpath(os.path.join(_HERE, "..", "..", ".."))
_CANDIDATES = [
    os.path.join(_REPO, "tools", "scctpkg"),
    os.path.expanduser(r"~\source\repos\SCCT_Versus_UE5_Bridge"),
]


def _import():
    for root in _CANDIDATES:
        if os.path.isfile(os.path.join(root, "scctpkg", "package.py")):
            if root not in sys.path:
                sys.path.insert(0, root)
            break
    import scctpkg.package as package  # noqa: E402
    return package


package = _import()
Package = package.Package
Reader = package.Reader
PackageError = package.PackageError
read_compact = package.read_compact


def properties_module():
    """scctpkg.properties, imported lazily (it loads a JSON table at import time)."""
    import scctpkg.properties as properties
    return properties


PACKAGES = ("Core", "Engine", "SBase", "GUI", "IpDrv")


def package_dir(game: str) -> str:
    """System/_PC_ under an install root (or the directory itself when it already holds the .u files)."""
    for cand in (os.path.join(game, "System", "_PC_"), os.path.join(game, "_PC_"), game):
        if os.path.isfile(os.path.join(cand, "Core.u")):
            return cand
    raise FileNotFoundError(f"no Core.u under {game}")
