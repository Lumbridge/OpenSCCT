"""Locates scctpkg, the reference install and the output folder.

scctpkg is taken from the vendored copy in ``tools/scctpkg`` when present, else from a Bridge
checkout (``SVM_BRIDGE_ROOT`` or the default path). Paths can be overridden with environment
variables so the modules work on another machine:

* ``SVM_GAME_DIR``    the reference install (contains ``Packages`` and ``System``; ``SVM_GAME_ROOT`` also works)
* ``SVM_BRIDGE_ROOT`` a Bridge checkout, used only when the vendored scctpkg is missing
* ``SVM_LOCAL``       output root (default ``<repo>/local``)
"""

from __future__ import annotations

import os
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
DEFAULT_GAME = Path(os.path.expanduser(r"~\Desktop\Enhanced SCCT Versus v4.0 Public Beta 3"))
DEFAULT_BRIDGE = Path(os.path.expanduser(r"~\source\repos\SCCT_Versus_UE5_Bridge"))


def _add_scctpkg() -> None:
    try:
        import scctpkg  # noqa: F401
        return
    except ImportError:
        pass
    candidates = [REPO / "tools" / "scctpkg", Path(os.environ.get("SVM_BRIDGE_ROOT", DEFAULT_BRIDGE))]
    for c in candidates:
        if (c / "scctpkg" / "package.py").is_file():
            sys.path.insert(0, str(c))
            return
    raise ImportError("scctpkg not found: expected tools/scctpkg/scctpkg or a Bridge checkout")


_add_scctpkg()

from scctpkg.package import Export, Package, PackageError, Reader, read_compact  # noqa: E402,F401


def game_root() -> Path:
    return Path(os.environ.get("SVM_GAME_DIR") or os.environ.get("SVM_GAME_ROOT") or DEFAULT_GAME)


def local_root() -> Path:
    return Path(os.environ.get("SVM_LOCAL", REPO / "local"))


def out_dir(*parts: str) -> Path:
    p = local_root().joinpath("formats", *parts)
    p.mkdir(parents=True, exist_ok=True)
    return p


def bytes_reader(data: bytes, names=None, obfuscated: bool = False) -> Reader:
    """A scctpkg Reader over raw bytes with an optional name table (for synthetic data in tests)."""

    class _Pkg:
        pass

    pkg = None
    if names is not None:
        pkg = _Pkg()
        pkg.names = list(names)
        pkg.names_obfuscated = obfuscated
    return Reader(data, 0, pkg)
