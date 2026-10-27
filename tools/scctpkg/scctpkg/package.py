"""Low-level reader for SCCT Versus package files.

Format notes (verified against the 4.0 Public Beta package set):

* Container: raw packages start with the Unreal tag ``9E 2A 83 C1``. Cooked
  maps (``.sdc``) and ``.utc`` textures are a zlib container:
  ``uint32 uncompressedSize, uint32 compressedSize, zlib stream``.
* Header: standard UE2 ``FPackageFileSummary`` (tag, version u16, licensee
  u16, flags, name/export/import count+offset, GUID, generations).
  Package versions seen: 172, 175 (stock UE2-era) and 300 (SCCT cooked).
* Name table: ``compact length`` then the string; in most packages the string
  bytes are XOR-obfuscated with ``(absolute file offset) & 0xFF``. Some
  packages (ports from the single-player game) store plain strings; the reader
  detects this from the first entry, which is always ``None``.
* Name references in the import/export tables and in serialized object data
  are TWO compact indices: the package-local name index followed by the
  cooked global name index (a load-time cache that we ignore).
* Import record: ClassPackage nref, ClassName nref, Outer int32, ObjectName nref.
* Export record: Class compact, Super compact, Outer int32, Name nref,
  Flags int32, Size compact, Offset compact (offset only present if size > 0).
"""

from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import List, Optional, Tuple

PACKAGE_TAG = 0x9E2A83C1


class PackageError(Exception):
    pass


def read_compact(d: bytes, p: int) -> Tuple[int, int]:
    """UE compact index: bit7 sign, bit6 more (first byte), then 7-bit groups."""
    b = d[p]
    p += 1
    n = b & 0x3F
    neg = b & 0x80
    more = b & 0x40
    shift = 6
    while more:
        b = d[p]
        p += 1
        n |= (b & 0x7F) << shift
        shift += 7
        more = b & 0x80
    return (-n if neg else n), p


class Reader:
    """Cursor over a bytes object with UE primitive helpers."""

    __slots__ = ("d", "p", "pkg", "base", "name_extra")

    def __init__(self, d: bytes, p: int = 0, pkg: "Package | None" = None, base: int = 0):
        self.d = d
        self.p = p
        self.pkg = pkg
        self.base = base  # absolute file offset of d[0], for XOR de-obfuscation
        self.name_extra = 0  # bytes spent on global name indices (excluded from tag sizes)

    def compact(self) -> int:
        v, self.p = read_compact(self.d, self.p)
        return v

    def u8(self) -> int:
        v = self.d[self.p]
        self.p += 1
        return v

    def i32(self) -> int:
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return v

    def u32(self) -> int:
        v = struct.unpack_from("<I", self.d, self.p)[0]
        self.p += 4
        return v

    def u16(self) -> int:
        v = struct.unpack_from("<H", self.d, self.p)[0]
        self.p += 2
        return v

    def i16(self) -> int:
        v = struct.unpack_from("<h", self.d, self.p)[0]
        self.p += 2
        return v

    def f32(self) -> float:
        v = struct.unpack_from("<f", self.d, self.p)[0]
        self.p += 4
        return v

    def i64(self) -> int:
        v = struct.unpack_from("<q", self.d, self.p)[0]
        self.p += 8
        return v

    def vec3(self) -> Tuple[float, float, float]:
        v = struct.unpack_from("<3f", self.d, self.p)
        self.p += 12
        return v

    def rot(self) -> Tuple[int, int, int]:
        v = struct.unpack_from("<3i", self.d, self.p)
        self.p += 12
        return v

    def raw(self, n: int) -> bytes:
        v = self.d[self.p:self.p + n]
        if len(v) != n:
            raise PackageError(f"read past end at {self.p}+{n}")
        self.p += n
        return v

    def fstring(self) -> str:
        """UE2 FString: compact length (negative = UTF-16) then chars incl NUL."""
        n = self.compact()
        if n == 0:
            return ""
        start = self.base + self.p  # absolute offset: strings use the same XOR as names
        obfuscated = self.pkg is not None and self.pkg.names_obfuscated
        if n < 0:
            raw = self.raw(-n * 2)
            if obfuscated:
                raw = bytes(v ^ ((start + j) & 0xFF) for j, v in enumerate(raw))
            return raw.decode("utf-16-le", errors="replace").rstrip("\0")
        raw = self.raw(n)
        if obfuscated:
            raw = bytes(v ^ ((start + j) & 0xFF) for j, v in enumerate(raw))
        return raw.decode("latin1").rstrip("\0")

    def name(self) -> str:
        """SCCT name reference: local compact index + global compact index."""
        idx = self.compact()
        before = self.p
        self.compact()  # cooked global name index, ignored
        self.name_extra += self.p - before
        if idx < 0 or self.pkg is None or idx >= len(self.pkg.names):
            raise PackageError(f"bad name index {idx} at {self.p}")
        return self.pkg.names[idx]

    def objref(self) -> int:
        """Object reference: >0 export index+1, <0 import index, 0 None."""
        return self.compact()

    def array_count(self) -> int:
        return self.compact()

    def tell(self) -> int:
        return self.p

    def seek(self, p: int) -> None:
        self.p = p

    def remaining(self) -> int:
        return len(self.d) - self.p


@dataclass
class Import:
    class_package: str
    class_name: str
    outer: int
    name: str


@dataclass
class Export:
    index: int  # 0-based
    class_ref: int
    super_ref: int
    outer: int
    name: str
    flags: int
    size: int
    offset: int
    class_name: str = ""
    class_package: str = ""
    full_name: str = ""


@dataclass
class Package:
    path: Path
    data: bytes
    version: int
    licensee: int
    flags: int
    names: List[str] = field(default_factory=list)
    imports: List[Import] = field(default_factory=list)
    exports: List[Export] = field(default_factory=list)
    names_obfuscated: bool = True
    compressed: bool = False

    # ---- construction -------------------------------------------------
    @classmethod
    def load(cls, path: "str | Path") -> "Package":
        path = Path(path)
        raw = path.read_bytes()
        if len(raw) < 16:
            raise PackageError(f"{path.name}: too small")
        compressed = False
        if struct.unpack_from("<I", raw, 0)[0] != PACKAGE_TAG:
            unc, comp = struct.unpack_from("<2I", raw, 0)
            if comp + 8 > len(raw) or unc == 0:
                raise PackageError(f"{path.name}: not an Unreal package")
            try:
                data = zlib.decompress(raw[8:8 + comp])
            except zlib.error as e:
                raise PackageError(f"{path.name}: zlib container failed: {e}")
            if len(data) != unc:
                raise PackageError(f"{path.name}: container size mismatch")
            compressed = True
            if struct.unpack_from("<I", data, 0)[0] != PACKAGE_TAG:
                raise PackageError(f"{path.name}: bad tag after decompression")
        else:
            data = raw
        tag, version, licensee, flags = struct.unpack_from("<IHHI", data, 0)
        pkg = cls(path=path, data=data, version=version, licensee=licensee,
                  flags=flags, compressed=compressed)
        pkg._read_tables()
        return pkg

    def _read_tables(self) -> None:
        d = self.data
        nc, no, ec, eo, ic, io = struct.unpack_from("<6i", d, 12)
        # Name table: XOR-obfuscated by position in most packages. Detect plain tables by their first entry, which is
        # usually "None" but not always (MHOtex_xbox.utx starts with "SpecificScaleCoeff"): a plain entry is printable
        # text ending in a NUL.
        n0, p0 = read_compact(d, no)
        first = d[p0:p0 + n0] if 0 < n0 <= 1024 else b""
        plain = len(first) > 1 and first[-1] == 0 and all(32 <= c < 127 for c in first[:-1])
        self.names_obfuscated = not plain
        p = no
        names = []
        for _ in range(nc):
            n, p = read_compact(d, p)
            if n <= 0 or n > 1024:
                raise PackageError(f"{self.path.name}: bad name length {n} at {p}")
            chunk = d[p:p + n]
            if self.names_obfuscated:
                chunk = bytes(v ^ ((p + j) & 0xFF) for j, v in enumerate(chunk))
            names.append(chunk.rstrip(b"\0").decode("latin1"))
            p += n + 4  # + name flags
        self.names = names

        r = Reader(d, io, self)
        imports = []
        for _ in range(ic):
            cp = r.name()
            cn = r.name()
            outer = r.i32()
            on = r.name()
            imports.append(Import(cp, cn, outer, on))
        self.imports = imports

        r = Reader(d, eo, self)
        exports = []
        for i in range(ec):
            class_ref = r.compact()
            super_ref = r.compact()
            outer = r.i32()
            name = r.name()
            flags = r.u32()
            size = r.compact()
            offset = r.compact() if size > 0 else 0
            exports.append(Export(i, class_ref, super_ref, outer, name, flags, size, offset))
        self.exports = exports
        for e in exports:
            e.class_name, e.class_package = self._class_of(e)
            e.full_name = self.full_name(e.index + 1)

    # ---- helpers ------------------------------------------------------
    def _class_of(self, e: Export) -> Tuple[str, str]:
        ref = e.class_ref
        if ref == 0:
            return "Class", "Core"
        if ref < 0:
            imp = self.imports[-ref - 1]
            outer = imp.outer
            pkg_name = ""
            if outer < 0:
                pkg_name = self.imports[-outer - 1].name
            return imp.name, pkg_name
        return self.exports[ref - 1].name, self.path.stem

    def object_name(self, ref: int) -> str:
        if ref == 0:
            return "None"
        if ref > 0:
            return self.exports[ref - 1].name
        return self.imports[-ref - 1].name

    def full_name(self, ref: int) -> str:
        """Dotted path like Package.Group.Object for an object reference."""
        parts = []
        guard = 0
        while ref != 0 and guard < 32:
            guard += 1
            if ref > 0:
                e = self.exports[ref - 1]
                parts.append(e.name)
                ref = e.outer
            else:
                i = self.imports[-ref - 1]
                parts.append(i.name)
                ref = i.outer
        if ref > 0 or (parts and ref == 0 and self.exports and parts[-1] != self.path.stem):
            # exports are rooted at this package
            if not parts or parts[-1] != self.path.stem:
                parts.append(self.path.stem)
        return ".".join(reversed(parts))

    def export_reader(self, e: Export) -> Reader:
        if e.size <= 0:
            return Reader(b"", 0, self)
        return Reader(self.data[e.offset:e.offset + e.size], 0, self, base=e.offset)

    def exports_of_class(self, *class_names: str) -> List[Export]:
        wanted = set(class_names)
        return [e for e in self.exports if e.class_name in wanted]

    def find_export(self, name: str, class_name: Optional[str] = None) -> Optional[Export]:
        """Unreal names are case-insensitive; exact case wins when both exist."""
        lower = name.lower()
        fallback = None
        for e in self.exports:
            if class_name is not None and e.class_name != class_name:
                continue
            if e.name == name:
                return e
            if fallback is None and e.name.lower() == lower:
                fallback = e
        return fallback
