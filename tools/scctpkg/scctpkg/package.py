"""Low-level reader for SCCT Versus package files.

Format notes (verified against the 4.0 Public Beta package set):

* Container: raw packages start with the Unreal tag ``9E 2A 83 C1``. Maps
  (``.sdc``) and ``.utc`` textures are a zlib container: a run of chunks
  ``uint32 uncompressedSize, uint32 compressedSize, zlib stream`` to the end of
  the file, joined in order. A normal save writes one chunk; the editor's
  autosaves (``MapsEd/Auto0..9.sdc``) write 15 MiB chunks.
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
* Some editor autosaves were written with all six table counts and offsets left
  at 0; the tables are still there and ``_recover_tables`` finds them.
* A map is an editor copy (``Packages/MapsEd``, with its brushes) or a cooked
  copy (``Packages/Maps``, what the game loads); see ``Package.layout``.
"""

from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass, field
from functools import cached_property
from pathlib import Path
from typing import List, Optional, Tuple

PACKAGE_TAG = 0x9E2A83C1
# Container sanity limits (the Reloaded editor's SdcBlockModel.h): a damaged size field must not allocate gigabytes.
MAX_UNPACKED = 1 << 30
MAX_INFLATE_RATIO = 1032


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


def _unpack_chunks(raw: bytes, file_name: str) -> bytes:
    """The package inside a zlib container: every chunk to the end of the file, each inflating to its stated size."""
    out: List[bytes] = []
    total = 0
    p = 0
    while p < len(raw):
        if p + 8 > len(raw):
            raise PackageError(f"{file_name}: truncated zlib chunk header at {p}")
        unc, comp = struct.unpack_from("<2I", raw, p)
        if unc == 0 or p + 8 + comp > len(raw):
            raise PackageError(f"{file_name}: not an Unreal package" if p == 0 else f"{file_name}: bad zlib chunk at {p}")
        if unc > comp * MAX_INFLATE_RATIO or total + unc > MAX_UNPACKED:
            raise PackageError(f"{file_name}: zlib chunk at {p} claims {unc} bytes from {comp}")
        try:
            chunk = zlib.decompress(raw[p + 8:p + 8 + comp])
        except zlib.error as e:
            raise PackageError(f"{file_name}: zlib container failed at {p}: {e}")
        if len(chunk) != unc:
            raise PackageError(f"{file_name}: container size mismatch at {p}")
        out.append(chunk)
        total += unc
        p += 8 + comp
    data = out[0] if len(out) == 1 else b"".join(out)
    if struct.unpack_from("<I", data, 0)[0] != PACKAGE_TAG:
        raise PackageError(f"{file_name}: bad tag after decompression")
    return data


def _encode_compact(v: int) -> bytes:
    """Inverse of read_compact."""
    a = abs(v)
    out = bytearray([(0x80 if v < 0 else 0) | (a & 0x3F) | (0x40 if a > 0x3F else 0)])
    a >>= 6
    while a:
        out.append((a & 0x7F) | (0x80 if a > 0x7F else 0))
        a >>= 7
    return bytes(out)


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
    recovered_tables: bool = False  # the summary's table counts were 0 and the tables were found by scanning

    # ---- construction -------------------------------------------------
    @classmethod
    def load(cls, path: "str | Path") -> "Package":
        path = Path(path)
        raw = path.read_bytes()
        if len(raw) < 16:
            raise PackageError(f"{path.name}: too small")
        compressed = False
        if struct.unpack_from("<I", raw, 0)[0] != PACKAGE_TAG:
            data = _unpack_chunks(raw, path.name)
            compressed = True
        else:
            data = raw
        tag, version, licensee, flags = struct.unpack_from("<IHHI", data, 0)
        pkg = cls(path=path, data=data, version=version, licensee=licensee,
                  flags=flags, compressed=compressed)
        try:
            pkg._read_tables()
        except PackageError:
            raise
        except (IndexError, struct.error, ValueError, OverflowError) as e:
            # Damaged or foreign tables (an offset past the end of the file, say): callers skip a PackageError.
            raise PackageError(f"{path.name}: unreadable tables: {type(e).__name__}: {e}") from e
        return pkg

    def _read_tables(self) -> None:
        d = self.data
        nc, no, ec, eo, ic, io = struct.unpack_from("<6i", d, 12)
        if (nc, no, ec, eo, ic, io) == (0, 0, 0, 0, 0, 0):
            self._recover_tables()
            return
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
        self._set_exports(exports)

    def _set_exports(self, exports: List[Export]) -> None:
        self.exports = exports
        for e in exports:
            e.class_name, e.class_package = self._class_of(e)
            e.full_name = self.full_name(e.index + 1)

    def _recover_tables(self) -> None:
        """Tables of a package whose summary counts and offsets are all 0 (the MapsEd autosaves of the 4.0 mapping
        install). The file order is summary, names (at 64), export data, imports, exports, so: read names from 64 while
        they look like names; find the import table by an import record Core.Package (or Core.Class) with outer 0; read
        imports one at a time and after each try to read export records from there. The split is right when the export
        records end exactly at the end of the file and every export's data lies before the import table."""
        d = self.data
        names: List[str] = []
        p = 64
        while p < len(d):
            n, q = read_compact(d, p)
            if not 1 < n <= 64 or q + n + 4 > len(d):
                break
            chunk = bytes(v ^ ((q + j) & 0xFF) for j, v in enumerate(d[q:q + n]))
            if chunk[-1] != 0 or not all(32 <= c < 127 for c in chunk[:-1]):
                break
            names.append(chunk[:-1].decode("latin1"))
            p = q + n + 4
        self.names, self.names_obfuscated = names, True
        core = _encode_compact(names.index("Core")) if "Core" in names else None
        start = p
        while core is not None:
            start = d.find(core, start + 1)
            if start < 0:
                break
            r = Reader(d, start, self)
            try:
                if r.name() != "Core" or r.name() not in ("Package", "Class") or r.i32() != 0:
                    continue
                r.name()
            except (PackageError, IndexError, struct.error):
                continue
            r = Reader(d, start, self)
            imports: List[Import] = []
            while True:
                try:
                    imp = Import(r.name(), r.name(), r.i32(), r.name())
                except (PackageError, IndexError, struct.error):
                    break
                if imp.outer > 0:  # an import's outer is another import or none
                    break
                imports.append(imp)
                exports = self._exports_to_end(r.p, len(imports), start)
                if exports is not None:
                    self.imports = imports
                    self._set_exports(exports)
                    self.recovered_tables = True
                    return
        raise PackageError(f"{self.path.name}: the summary has no tables and none were found")

    def _exports_to_end(self, p: int, import_count: int, data_end: int) -> Optional[List[Export]]:
        """Export records from p that end exactly at the end of the file, each with its data before data_end."""
        d = self.data
        r = Reader(d, p, self)
        exports: List[Export] = []
        try:
            while r.p < len(d):
                class_ref = r.compact()
                super_ref = r.compact()
                outer = r.i32()
                name = r.name()
                flags = r.u32()
                size = r.compact()
                offset = r.compact() if size > 0 else 0
                if class_ref < -import_count or size < 0 or offset < 0 or offset + size > data_end:
                    return None
                exports.append(Export(len(exports), class_ref, super_ref, outer, name, flags, size, offset))
        except (PackageError, IndexError, struct.error):
            return None
        if r.p != len(d) or not exports or any(e.class_ref > len(exports) for e in exports):
            return None
        return exports

    @cached_property
    def layout(self) -> str:
        """"editor" for an editor copy (what the stock editor saves to Packages/MapsEd: brushes, a source FPoly on every
        BSP surface, the lightmap array) or "cooked" for a cooked copy (Packages/Maps, what the game loads). Nothing in
        the file records it and folders are only a hint: see bsp.detect_layout."""
        from .bsp import detect_layout  # noqa: PLC0415 - bsp imports this module

        return detect_layout(self)

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
