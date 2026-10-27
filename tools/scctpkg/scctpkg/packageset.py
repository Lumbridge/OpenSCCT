"""Resolves object references across packages (imports -> other files).

A `PackageSet` indexes every package under the game's Packages folder by
stem (case-insensitive) and loads them lazily. `resolve(pkg, ref)` follows
an import to the package that owns the object and returns (package, export).
"""

from __future__ import annotations

from pathlib import Path
from typing import Dict, Optional, Tuple

from .package import Export, Package, PackageError

PACKAGE_EXTS = (".usx", ".utx", ".utc", ".ukx", ".uax", ".sdc", ".u")


class PackageSet:
    def __init__(self, roots):
        self.files: Dict[str, Path] = {}
        self.loaded: Dict[str, Optional[Package]] = {}
        for root in roots:
            root = Path(root)
            for f in root.rglob("*"):
                if f.suffix.lower() in PACKAGE_EXTS and f.is_file():
                    key = f.stem.lower()
                    # prefer non-map packages when names collide (maps live in Maps/)
                    if key not in self.files or f.suffix.lower() != ".sdc":
                        self.files.setdefault(key, f)
                        if f.suffix.lower() != ".sdc":
                            self.files[key] = f

    def get(self, name: str) -> Optional[Package]:
        key = name.lower()
        if key in self.loaded:
            return self.loaded[key]
        path = self.files.get(key)
        pkg = None
        if path is not None:
            try:
                pkg = Package.load(path)
            except PackageError:
                pkg = None
        self.loaded[key] = pkg
        return pkg

    def register(self, pkg: Package) -> None:
        self.loaded[pkg.path.stem.lower()] = pkg

    @staticmethod
    def root_package_of_import(pkg: Package, ref: int) -> Optional[str]:
        """Walk an import's outer chain to the top-level package name."""
        guard = 0
        while ref < 0 and guard < 32:
            guard += 1
            imp = pkg.imports[-ref - 1]
            if imp.outer == 0:
                return imp.name
            ref = imp.outer
        return None

    def resolve(self, pkg: Package, ref: int) -> Tuple[Optional[Package], Optional[Export]]:
        """Return (owning package, export) for an object reference in pkg."""
        if ref == 0:
            return None, None
        if ref > 0:
            return pkg, pkg.exports[ref - 1]
        imp = pkg.imports[-ref - 1]
        root = self.root_package_of_import(pkg, ref)
        if root is None:
            return None, None
        other = self.get(root)
        if other is None:
            return None, None
        # match by name and class; groups are ignored (names are unique per package in practice)
        e = other.find_export(imp.name, imp.class_name)
        if e is None:
            e = other.find_export(imp.name)
        return (other, e) if e is not None else (other, None)

    def import_path(self, pkg: Package, ref: int) -> str:
        """Human readable Package.Name for any ref."""
        if ref == 0:
            return "None"
        if ref > 0:
            return f"{pkg.path.stem}.{pkg.exports[ref - 1].name}"
        root = self.root_package_of_import(pkg, ref) or "?"
        return f"{root}.{pkg.imports[-ref - 1].name}"
