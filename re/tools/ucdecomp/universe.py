"""A set of loaded packages with cross-package reference resolution and the native function table."""

from __future__ import annotations

import os
from typing import Dict, List, Optional, Tuple

from .loader import Class, Enum, Field, Function, LoadedPackage, Struct
from .pkgload import PACKAGES, package_dir

PROBE_MIN = 300   # global name index of the first probe function name (Spawned); 64 probes follow


class Universe:
    def __init__(self, packages: Dict[str, LoadedPackage]):
        self.packages = packages
        self._by_path: Dict[str, Tuple[LoadedPackage, int]] = {}
        for lp in packages.values():
            for ref in lp.fields:
                self._by_path.setdefault(lp.path_of(ref).lower(), (lp, ref))
            for e in lp.pkg.exports:
                self._by_path.setdefault(lp.path_of(e.index + 1).lower(), (lp, e.index + 1))
        self.natives: Dict[int, Function] = {}
        self.native_conflicts: List[Tuple[int, str, str]] = []
        for name in PACKAGES:
            lp = packages.get(name)
            if lp is None:
                continue
            for f in lp.fields.values():
                if isinstance(f, Function) and f.native:
                    prev = self.natives.get(f.native)
                    if prev is not None and prev.path != f.path:
                        self.native_conflicts.append((f.native, prev.path, f.path))
                        continue
                    self.natives[f.native] = f
        # global name index -> text, from every package
        self.global_names: Dict[int, str] = {}
        for lp in packages.values():
            for a, g in lp.global_name.items():
                self.global_names.setdefault(g, lp.name_of(a))

    @classmethod
    def load(cls, game: str, names=PACKAGES) -> "Universe":
        d = package_dir(game)
        pk = {}
        for n in names:
            p = os.path.join(d, n + ".u")
            if os.path.isfile(p):
                pk[n] = LoadedPackage(p)
        return cls(pk)

    # resolution ---------------------------------------------------------------------------------
    def resolve(self, lp: LoadedPackage, ref: int) -> Tuple[Optional[LoadedPackage], Optional[Field]]:
        """Field for an object reference made from package lp (exports directly, imports by path)."""
        if ref == 0:
            return None, None
        if ref > 0:
            return lp, lp.fields.get(ref)
        hit = self._by_path.get(lp.path_of(ref).lower())
        if hit is None:
            return None, None
        tp, tref = hit
        return tp, tp.fields.get(tref)

    def field(self, lp: LoadedPackage, ref: int) -> Optional[Field]:
        return self.resolve(lp, ref)[1]

    def find(self, path: str) -> Optional[Field]:
        hit = self._by_path.get(path.lower())
        if hit is None:
            return None
        return hit[0].fields.get(hit[1])

    def super_of(self, s: Struct) -> Optional[Struct]:
        f = self.field(s.pkg, s.super_field) if s.super_field else None
        return f if isinstance(f, Struct) else None

    def class_chain(self, c: Class) -> List[Class]:
        out = []
        seen = set()
        while c is not None and id(c) not in seen:
            seen.add(id(c))
            out.append(c)
            c = self.super_of(c)
        return out

    def owner_class(self, f: Field) -> Optional[Class]:
        lp = f.pkg
        ref = f.outer
        while ref > 0:
            o = lp.fields.get(ref)
            if isinstance(o, Class):
                return o
            ref = lp.pkg.exports[ref - 1].outer
        return None

    def members(self, s: Struct) -> List[Field]:
        return [s.pkg.fields[r] for r in s.fields if r in s.pkg.fields]

    def find_member(self, s: Struct, name: str, kinds=None) -> Optional[Field]:
        """Look a member up in a struct and its supers (case-insensitive)."""
        low = name.lower()
        cur = s
        guard = 0
        while cur is not None and guard < 64:
            guard += 1
            for m in self.members(cur):
                if m.name.lower() == low and (kinds is None or m.kind in kinds):
                    return m
            cur = self.super_of(cur)
        return None

    def enums_larger_than(self, value: int) -> List[Enum]:
        if not hasattr(self, "_enums"):
            self._enums = [f for lp in self.packages.values() for f in lp.fields.values() if isinstance(f, Enum)]
        return [e for e in self._enums if value < len(e.values)]

    def probe_name(self, bit: int) -> str:
        return self.global_names.get(PROBE_MIN + bit, "Probe%d" % bit)
