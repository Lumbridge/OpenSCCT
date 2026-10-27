"""Repository locations and the default game and output roots."""

from __future__ import annotations

import os
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SCCTPKG_ROOT = REPO_ROOT / "tools" / "scctpkg"
SPEC_ROOT = REPO_ROOT / "spec" / "svmap" / "1.0"
SCHEMA_ROOT = SPEC_ROOT / "schemas"
TESTBOX_FIXTURE = REPO_ROOT / "fixtures" / "testbox"

DEFAULT_GAME_ROOT = Path(os.path.expanduser(r"~\Desktop\Enhanced SCCT Versus v4.0 Public Beta 3"))


def default_game_root() -> Path:
    env = os.environ.get("SVM_GAME_ROOT")
    return Path(env) if env else DEFAULT_GAME_ROOT


def default_out_root() -> Path:
    env = os.environ.get("SVM_CONTENT")
    return Path(env) if env else REPO_ROOT / ".content"


def import_scctpkg(require_class_defaults: bool = False):
    """Import the vendored scctpkg (tools/scctpkg/scctpkg), never another copy on sys.path.

    class_defaults.json is game-derived and not tracked; only scctpkg.level reads it (map and render export), so
    callers that use level pass require_class_defaults=True. The plain readers work without it (synthetic tests, CI).
    """
    root = str(SCCTPKG_ROOT)
    if root not in sys.path:
        sys.path.insert(0, root)
    import scctpkg  # noqa: PLC0415

    where = Path(scctpkg.__file__).resolve().parent
    if where != (SCCTPKG_ROOT / "scctpkg").resolve():
        raise ImportError(f"scctpkg imported from {where}, expected the vendored copy under {SCCTPKG_ROOT}")
    if require_class_defaults and not (where / "class_defaults.json").exists():
        # An installed builder: made from the content root's shared/classes.json (the classes step).
        from .sources import classdefaults_dump  # noqa: PLC0415
        if not classdefaults_dump.ensure(where, default_out_root()):
            raise ImportError("tools/scctpkg/scctpkg/class_defaults.json is missing: run the classes step first "
                              "(or python tools/scctpkg/sync.py)")
    return scctpkg


def is_within(path: Path, root: Path) -> bool:
    try:
        Path(path).resolve().relative_to(Path(root).resolve())
        return True
    except ValueError:
        return False
