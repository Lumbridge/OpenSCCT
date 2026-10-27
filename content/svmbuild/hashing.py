"""File digests with a stat cache, so unchanged inputs are not re-read on every build."""

from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
from typing import Iterable

_CHUNK = 1 << 20


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while True:
            block = f.read(_CHUNK)
            if not block:
                break
            h.update(block)
    return h.hexdigest()


class FileHasher:
    """sha256 of files, remembered by (size, mtime_ns) in a JSON file between runs."""

    def __init__(self, store: Path | None = None, trust_stat: bool = True, read_only: bool = False):
        self.store = store
        self.trust_stat = trust_stat
        # A worker process reads the memo but never writes it; the parent merges its new entries.
        self.read_only = read_only
        self._known: dict[str, list] = {}
        self._new: dict[str, list] = {}
        self._dirty = False
        if store is not None and store.exists():
            try:
                self._known = json.loads(store.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                self._known = {}

    def sha256(self, path: Path) -> str:
        path = Path(path)
        st = path.stat()
        key = os.path.normcase(str(path.resolve()))
        hit = self._known.get(key)
        if self.trust_stat and hit and hit[0] == st.st_size and hit[1] == st.st_mtime_ns:
            return hit[2]
        digest = sha256_file(path)
        self._known[key] = [st.st_size, st.st_mtime_ns, digest]
        self._new[key] = self._known[key]
        self._dirty = True
        return digest

    def new_entries(self) -> dict[str, list]:
        """Digests computed by this hasher (not read from the memo file)."""
        return dict(self._new)

    def merge(self, entries: dict[str, list]) -> None:
        if entries:
            self._known.update(entries)
            self._dirty = True

    def maybe(self, path: Path) -> str | None:
        """Digest, or None when the file does not exist."""
        return self.sha256(path) if Path(path).is_file() else None

    def tree(self, root: Path, files: Iterable[Path]) -> str:
        """Digest of a set of files under root: sha256 of their sha256sum-style listing."""
        rows = sorted((p.relative_to(root).as_posix(), self.sha256(p)) for p in files)
        listing = "".join(f"{d}  {r}\n" for r, d in rows)
        return hashlib.sha256(listing.encode("utf-8")).hexdigest()

    def save(self) -> None:
        if self.store is None or not self._dirty or self.read_only:
            return
        self.store.parent.mkdir(parents=True, exist_ok=True)
        tmp = self.store.with_name(f"{self.store.name}.{os.getpid()}.tmp")
        tmp.write_text(json.dumps(self._known, sort_keys=True), encoding="utf-8")
        os.replace(tmp, self.store)
        self._dirty = False
