"""Exact class default objects from the script packages (System/_PC_/*.u, package version 300).

A class export is read field by field, the way the engine loads it, so the default properties start
exactly where they begin (no searching for a start offset):

    UField    objref SuperField, objref Next          (a UClass object has no tagged property list first)
    UStruct   objref ScriptText, objref Children, name FriendlyName, objref CppText (version >= 120),
              i32 Line, i32 TextPos, i32 ScriptSize (bytes in memory), then the bytecode, token by token
    UState    u64 ProbeMask, u64 IgnoreMask, u16 LabelTableOffset, u32 StateFlags
    UClass    u32 ClassFlags, 16-byte ClassGuid, compact n + n x (objref Class, i32 Deep, u32 ScriptTextCRC)
              Dependencies, compact n + n x name PackageImports, objref ClassWithin (version >= 62),
              name ClassConfigName (version >= 62), compact n + n x name HideCategories (version >= 99),
              then the class defaults as a tagged property list ending in None.

The bytecode has to be walked: names and object references are compact on disk but 4 bytes in memory, so
the disk length is not ScriptSize. SCCT renumbers the native tokens (extended natives 0x46..0x55, first
native 0x56; stock UE2 uses 0x60 and 0x70); the full operand table is SCRIPT_OPS below.

Properties (for typing array elements, enum names and struct members):
    UProperty  UField, i32 ArrayDim, u32 PropertyFlags, name Category, u16 RepOffset if flags & 0x20 (CPF_Net)
    then ByteProperty objref Enum; ObjectProperty objref Class; ClassProperty + objref MetaClass;
    StructProperty objref Struct; ArrayProperty objref Inner; FixedArrayProperty objref Inner, i32 Count;
    MapProperty objref Key, objref Value; DelegateProperty objref Function; BoolProperty i32 (SCCT extra).

Tagged values: Vector, Rotator and Color structs are stored natively (the struct item serializer skips the
tagged form for those three names); every other struct is a nested tagged list.

Sources: ChaosTheory_Editor.exe UField 0x10fb4f00, UStruct 0x10fb60d0, SerializeExpr 0x10fb64e0,
UState 0x10fb6350, UFunction 0x10fb6b10, UClass 0x10fb8c60, UProperty 0x10fe1790 and subclasses,
FPropertyTag 0x10fb5cc0, UStructProperty::SerializeItem 0x10fe4400.
"""

from __future__ import annotations

import argparse
import json
import os
import struct
import sys
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from ._env import Package, PackageError, Reader, game_root, local_root, out_dir

DEFAULT_PACKAGES = ["Core", "Engine", "SBase", "GUI", "IpDrv"]
NATIVE_STRUCTS = {"vector": "vec", "rotator": "rot", "color": "color"}

CPF_NET = 0x20
FUNC_NET = 0x40


class ClassFormatError(PackageError):
    pass


# ---- bytecode --------------------------------------------------------------------------------
# Operand kinds: E = sub-expression, P = parameters until EX_EndFunctionParms (0x16), O = object ref
# (4 bytes in memory), N = name (4), B = byte, W = word, I = int, F = float, S = NUL-terminated string,
# L = label table, C = case (word, then an expression unless the word is 0xffff), X = bad token.
SCRIPT_OPS: Dict[int, str] = {}
for _t in (0x08, 0x0B, 0x16, 0x17, 0x25, 0x26, 0x27, 0x28, 0x2A, 0x2D, 0x30, 0x31):
    SCRIPT_OPS[_t] = ""
for _t in (0x00, 0x01, 0x02, 0x29, 0x20):
    SCRIPT_OPS[_t] = "O"
SCRIPT_OPS.update({
    0x06: "W", 0x1C: "OP", 0x1B: "NP", 0x38: "NP", 0x43: "ON", 0x12: "EWBE", 0x19: "EWBE",
    0x05: "BE", 0x39: "BE", 0x40: "EEE", 0x41: "EEE", 0x11: "EEEE", 0x1D: "I", 0x1F: "S",
    0x22: "III", 0x23: "FFF", 0x1E: "F", 0x24: "B", 0x2C: "B", 0x07: "WE", 0x09: "WE", 0x18: "WE",
    0x2F: "EW", 0x0A: "C", 0x0C: "L", 0x32: "OEE", 0x33: "OEE", 0x13: "OE", 0x2E: "OE", 0x36: "OE",
    0x21: "N", 0x44: "N",
})
for _t in (0x0F, 0x10, 0x14, 0x1A, 0x45):
    SCRIPT_OPS[_t] = "EE"
for _t in (0x04, 0x0D, 0x0E, 0x37):
    SCRIPT_OPS[_t] = "E"
for _t in (0x03, 0x15, 0x2B, 0x34, 0x35, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x42):
    SCRIPT_OPS[_t] = "X"
EX_END_PARMS = 0x16
EX_EXTENDED_NATIVE = 0x46
EX_FIRST_NATIVE = 0x56


class ScriptWalker:
    """Walks serialized bytecode, counting the in-memory size the way SerializeExpr does."""

    def __init__(self, r: Reader):
        self.r = r
        self.mem = 0
        self.tokens = 0

    def expr(self, depth: int = 0) -> int:
        if depth > 512:
            raise ClassFormatError("bytecode nests too deep")
        r = self.r
        tok = r.u8()
        self.mem += 1
        self.tokens += 1
        if tok >= EX_FIRST_NATIVE:
            self._params(depth)
            return tok
        if tok >= EX_EXTENDED_NATIVE:
            r.u8()
            self.mem += 1
            self._params(depth)
            return tok
        ops = SCRIPT_OPS[tok]
        for op in ops:
            if op == "E":
                self.expr(depth + 1)
            elif op == "P":
                self._params(depth)
            elif op == "O":
                r.compact()
                self.mem += 4
            elif op == "N":
                r.name()
                self.mem += 4
            elif op == "B":
                r.u8()
                self.mem += 1
            elif op == "W":
                r.u16()
                self.mem += 2
            elif op in "IF":
                r.raw(4)
                self.mem += 4
            elif op == "S":
                while True:
                    self.mem += 1
                    if r.u8() == 0:
                        break
            elif op == "C":
                w = r.u16()
                self.mem += 2
                if w != 0xFFFF:
                    self.expr(depth + 1)
            elif op == "L":
                while True:
                    n = r.name()
                    r.raw(4)
                    self.mem += 8
                    if n == "None":
                        break
            elif op == "X":
                raise ClassFormatError(f"bad bytecode token 0x{tok:02x} at {r.p - 1}")
        return tok

    def _params(self, depth: int) -> None:
        while self.expr(depth + 1) != EX_END_PARMS:
            pass

    def run(self, script_size: int) -> int:
        start = self.r.p
        while self.mem < script_size:
            self.expr()
        if self.mem != script_size:
            raise ClassFormatError(f"bytecode ran {self.mem - script_size} bytes past ScriptSize {script_size}")
        return self.r.p - start


# ---- object model ----------------------------------------------------------------------------

@dataclass
class Field:
    pkg: "Package"
    export: Any
    kind: str
    super_ref: int = 0
    next_ref: int = 0
    data: Dict[str, Any] = field(default_factory=dict)

    @property
    def name(self) -> str:
        return self.export.name

    @property
    def path(self) -> str:
        return export_path(self.pkg, self.export)


def read_field(r: Reader) -> Tuple[int, int]:
    return r.compact(), r.compact()


def read_struct(r: Reader, version: int, d: dict) -> None:
    d["script_text"] = r.compact()
    d["children"] = r.compact()
    d["friendly_name"] = r.name()
    if version >= 0x78:
        d["cpp_text"] = r.compact()
    d["line"] = r.i32()
    d["text_pos"] = r.i32()
    size = r.i32()
    if size < 0 or size > 1 << 24:
        raise ClassFormatError(f"implausible ScriptSize {size}")
    d["script_size"] = size
    w = ScriptWalker(r)
    d["script_disk"] = w.run(size)
    d["script_tokens"] = w.tokens


def read_state(r: Reader, version: int, d: dict) -> None:
    read_struct(r, version, d)
    d["probe_mask"] = struct.unpack("<Q", r.raw(8))[0]
    d["ignore_mask"] = struct.unpack("<Q", r.raw(8))[0]
    d["label_table_offset"] = r.u16()
    d["state_flags"] = r.u32()


def read_function(r: Reader, version: int, d: dict) -> None:
    read_struct(r, version, d)
    if version <= 0x3F:
        r.u16()
    d["native"] = r.u16()
    if version <= 0x3F:
        r.u8()
    d["oper_precedence"] = r.u8()
    if version <= 0x3F:
        r.u16()
    d["function_flags"] = r.u32()
    if d["function_flags"] & FUNC_NET:
        d["rep_offset"] = r.u16()


def read_class_header(r: Reader, version: int, d: dict) -> None:
    read_state(r, version, d)
    if version <= 0x3D:
        r.i32()
    d["class_flags"] = r.u32()
    d["guid"] = r.raw(16).hex()
    deps = []
    for _ in range(r.compact()):
        deps.append((r.compact(), r.i32(), r.u32()))
    d["dependencies"] = deps
    d["package_imports"] = [r.name() for _ in range(r.compact())]
    if version >= 0x3E:
        d["within"] = r.compact()
        d["config_name"] = r.name()
    if version >= 0x63:
        d["hide_categories"] = [r.name() for _ in range(r.compact())]
    d["defaults_offset"] = r.p


PROPERTY_EXTRA = {
    "ByteProperty": ("enum",), "ObjectProperty": ("cls",), "ClassProperty": ("cls", "meta"),
    "StructProperty": ("struct",), "ArrayProperty": ("inner",), "FixedArrayProperty": ("inner", "#count"),
    "MapProperty": ("key", "value"), "DelegateProperty": ("function",), "BoolProperty": (),
    "IntProperty": (), "FloatProperty": (), "NameProperty": (), "StrProperty": (), "StringProperty": (),
    "PointerProperty": (),
}


def read_property(r: Reader, kind: str, d: dict) -> None:
    d["array_dim"] = r.i32()
    d["flags"] = r.u32()
    d["category"] = r.name()
    if d["flags"] & CPF_NET:
        d["rep_offset"] = r.u16()
    for k in PROPERTY_EXTRA[kind]:
        if k.startswith("#"):
            d[k[1:]] = r.i32()
        else:
            d[k] = r.compact()


# ---- universe of packages --------------------------------------------------------------------

def object_path(pkg: Package, ref: int) -> str:
    """Dotted path of an object reference. Exports are rooted at their package; an import's outer chain
    already ends at its package (scctpkg's full_name prefixes imports with the current package)."""
    if ref > 0:
        return export_path(pkg, pkg.exports[ref - 1])
    parts = []
    guard = 0
    while ref < 0 and guard < 32:
        guard += 1
        imp = pkg.imports[-ref - 1]
        parts.append(imp.name)
        ref = imp.outer
    if ref > 0:
        parts.append(export_path(pkg, pkg.exports[ref - 1]))
    return ".".join(reversed(parts))


def export_path(pkg: Package, e) -> str:
    """Package.Outer.Name for an export (scctpkg drops the package when an object shares its name)."""
    parts = [e.name]
    ref = e.outer
    guard = 0
    while ref > 0 and guard < 32:
        guard += 1
        o = pkg.exports[ref - 1]
        parts.append(o.name)
        ref = o.outer
    parts.append(pkg.path.stem)
    return ".".join(reversed(parts))


class Universe:
    def __init__(self, root: Optional[Path] = None):
        self.root = Path(root) if root else game_root() / "System" / "_PC_"
        self.pkgs: Dict[str, Package] = {}
        self.by_path: Dict[str, Dict[str, Any]] = {}
        self.fields: Dict[Tuple[str, int], Field] = {}
        self.errors: List[Tuple[str, str]] = []

    def package(self, name: str) -> Optional[Package]:
        key = name.lower()
        if key in self.pkgs:
            return self.pkgs[key]
        p = self.root / f"{name}.u"
        if not p.exists():
            self.pkgs[key] = None
            return None
        pkg = Package.load(p)
        self.pkgs[key] = pkg
        self.by_path[key] = {export_path(pkg, e).lower(): e for e in pkg.exports}
        return pkg

    def resolve(self, pkg: Package, ref: int) -> Optional[Tuple[Package, Any]]:
        """Object reference -> (package, export), following imports into the loaded script packages."""
        if ref == 0:
            return None
        if ref > 0:
            return pkg, pkg.exports[ref - 1]
        path = object_path(pkg, ref)
        top = path.split(".")[0]
        other = self.package(top)
        if other is None:
            return None
        e = self.by_path[top.lower()].get(path.lower())
        return (other, e) if e is not None else None

    def path(self, pkg: Package, ref: int) -> Optional[str]:
        return None if ref == 0 else object_path(pkg, ref)

    def field(self, pkg: Package, e) -> Field:
        key = (pkg.path.stem.lower(), e.index)
        f = self.fields.get(key)
        if f is not None:
            return f
        f = Field(pkg, e, e.class_name)
        self.fields[key] = f
        r = pkg.export_reader(e)
        try:
            k = e.class_name
            if k != "Class":
                # Every object but a UClass starts with its own tagged property list (just None for fields).
                from scctpkg.properties import read_properties
                own = read_properties(r)
                if own:
                    f.data["object_props"] = dict(own)
            f.super_ref, f.next_ref = read_field(r)
            if k == "Class":
                read_class_header(r, pkg.version, f.data)
            elif k == "State":
                read_state(r, pkg.version, f.data)
            elif k == "Function":
                read_function(r, pkg.version, f.data)
            elif k == "Struct":
                read_struct(r, pkg.version, f.data)
                f.data["defaults_offset"] = r.p
            elif k in PROPERTY_EXTRA:
                read_property(r, k, f.data)
            elif k == "Enum":
                f.data["names"] = [r.name() for _ in range(r.compact())]
            elif k == "Const":
                f.data["value"] = r.fstring()
            f.data["end"] = r.p
            f.data["size"] = len(r.d)
        except Exception as ex:
            f.data["error"] = f"{type(ex).__name__}: {ex}"
            self.errors.append((e.full_name, f.data["error"]))
        return f

    def field_ref(self, pkg: Package, ref: int) -> Optional[Field]:
        res = self.resolve(pkg, ref)
        if res is None:
            return None
        return self.field(*res)

    def children(self, f: Field) -> List[Field]:
        out = []
        ref = f.data.get("children", 0)
        guard = 0
        while ref and guard < 100000:
            guard += 1
            c = self.field_ref(f.pkg, ref)
            if c is None:
                break
            out.append(c)
            ref = c.next_ref
        return out

    def properties(self, f: Field) -> Dict[str, Field]:
        """Name (lower case) -> property, own first, then up the super chain (FindField order)."""
        cache = f.data.get("_props")
        if cache is not None:
            return cache
        props: Dict[str, Field] = {}
        sup = self.field_ref(f.pkg, f.super_ref) if f.super_ref else None
        if sup is not None and sup is not f:
            props.update(self.properties(sup))
        for c in self.children(f):
            if c.kind in PROPERTY_EXTRA:
                props[c.name.lower()] = c
        f.data["_props"] = props
        return props

    def ordered_properties(self, f: Field) -> List[Field]:
        """Every property of a class or struct, base classes first."""
        out: List[Field] = []
        sup = self.field_ref(f.pkg, f.super_ref) if f.super_ref else None
        if sup is not None and sup is not f:
            out.extend(self.ordered_properties(sup))
        out.extend(c for c in self.children(f) if c.kind in PROPERTY_EXTRA)
        return out


# ---- tagged properties -----------------------------------------------------------------------

@dataclass
class TagStats:
    tags: int = 0
    size_mismatch: int = 0
    unknown: int = 0
    type_mismatch: int = 0
    notes: List[str] = field(default_factory=list)


def _tag_size(r: Reader, code: int) -> int:
    if code < 5:
        return (1, 2, 4, 12, 16)[code]
    if code == 5:
        return r.u8()
    if code == 6:
        return r.u16()
    return r.i32()


def _array_index(r: Reader) -> int:
    b = r.u8()
    if b < 0x80:
        return b
    if b & 0xC0 == 0x80:
        return ((b & 0x7F) << 8) | r.u8()
    return ((b & 0x3F) << 24) | (r.u8() << 16) | (r.u8() << 8) | r.u8()


TAG_KINDS = {1: "ByteProperty", 2: "IntProperty", 3: "BoolProperty", 4: "FloatProperty", 5: "ObjectProperty",
             6: "NameProperty", 7: "StringProperty", 8: "ClassProperty", 9: "ArrayProperty", 10: "StructProperty",
             13: "StrProperty", 14: "MapProperty", 15: "FixedArrayProperty"}
# Tag types the packages use for property kinds that have no type id of their own: a ClassProperty is
# tagged as an object (5), and a DelegateProperty takes the old fixed-string id (7).
TAG_ALIASES = {("ObjectProperty", "ClassProperty"), ("StringProperty", "DelegateProperty")}


class Decoder:
    def __init__(self, uni: Universe, stats: Optional[TagStats] = None):
        self.u = uni
        self.stats = stats or TagStats()

    def native_struct(self, r: Reader, kind: str):
        if kind == "vec":
            x, y, z = r.vec3()
            return {"X": x, "Y": y, "Z": z}
        if kind == "rot":
            p, y, rr = r.rot()
            return {"Pitch": p, "Yaw": y, "Roll": rr}
        b, g, rr, a = r.raw(4)
        return {"B": b, "G": g, "R": rr, "A": a}

    def item(self, r: Reader, pkg: Package, prop: Field, depth: int):
        """One value of a property (SerializeItem), typed by the property."""
        k = prop.kind
        d = prop.data
        if k == "ByteProperty":
            v = r.u8()
            en = self.u.field_ref(prop.pkg, d.get("enum", 0)) if d.get("enum") else None
            if en is not None and "names" in en.data:
                names = en.data["names"]
                return {"value": v, "enum": names[v] if v < len(names) else None}
            return v
        if k == "IntProperty":
            return r.i32()
        if k == "FloatProperty":
            return r.f32()
        if k in ("ObjectProperty", "ClassProperty"):
            return self.u.path(pkg, r.compact())
        if k == "NameProperty":
            return r.name()
        if k in ("StrProperty", "StringProperty"):
            return r.fstring()
        if k == "DelegateProperty":
            return {"object": self.u.path(pkg, r.compact()), "function": r.name()}
        if k == "StructProperty":
            st = self.u.field_ref(prop.pkg, d.get("struct", 0))
            sname = st.name.lower() if st else (self.u.path(prop.pkg, d.get("struct", 0)) or "").split(".")[-1].lower()
            if sname in NATIVE_STRUCTS:
                return self.native_struct(r, NATIVE_STRUCTS[sname])
            if st is None:
                raise ClassFormatError(f"struct of {prop.path} not found")
            return self.tagged(r, pkg, st, depth + 1)
        if k == "ArrayProperty":
            inner = self.u.field_ref(prop.pkg, d.get("inner", 0))
            if inner is None:
                raise ClassFormatError(f"inner property of {prop.path} not found")
            n = r.compact()
            if n < 0 or n > r.remaining():
                raise ClassFormatError(f"bad array count {n} for {prop.path}")
            return [self.item(r, pkg, inner, depth + 1) for _ in range(n)]
        if k == "FixedArrayProperty":
            inner = self.u.field_ref(prop.pkg, d.get("inner", 0))
            return [self.item(r, pkg, inner, depth + 1) for _ in range(d.get("count", 0))]
        if k == "BoolProperty":
            return bool(r.u8())
        raise ClassFormatError(f"no item reader for {k} ({prop.path})")

    def tagged(self, r: Reader, pkg: Package, owner: Field, depth: int = 0) -> Dict[str, Any]:
        """A tagged property list ending in None. Static arrays come back as {index: value} dicts."""
        if depth > 16:
            raise ClassFormatError("struct nesting too deep")
        props = self.u.properties(owner)
        out: Dict[str, Any] = {}
        while True:
            name = r.name()
            if name == "None":
                return out
            info = r.u8()
            ptype = info & 0x0F
            flag = bool(info & 0x80)
            sname = r.name() if ptype == 10 else None
            size = _tag_size(r, (info >> 4) & 7)
            index = _array_index(r) if flag and ptype != 3 else 0
            self.stats.tags += 1
            prop = props.get(name.lower())
            start, extra0 = r.p, r.name_extra
            if ptype == 3:
                value: Any = flag
                if prop is not None and prop.kind != "BoolProperty":
                    self.stats.type_mismatch += 1
            elif prop is None:
                self.stats.unknown += 1
                self.stats.notes.append(f"{owner.path}: unknown property {name}, {size} bytes skipped")
                value = {"_unknown_type": TAG_KINDS.get(ptype, ptype), "_raw": r.raw(size).hex()}
            else:
                if TAG_KINDS.get(ptype) != prop.kind and (TAG_KINDS.get(ptype), prop.kind) not in TAG_ALIASES:
                    self.stats.type_mismatch += 1
                    self.stats.notes.append(f"{owner.path}.{name}: tag {TAG_KINDS.get(ptype, ptype)} vs {prop.kind}")
                if ptype == 10 and prop.kind == "StructProperty":
                    st = self.u.field_ref(prop.pkg, prop.data.get("struct", 0))
                    if st is not None and sname and st.name.lower() != sname.lower():
                        self.stats.notes.append(f"{owner.path}.{name}: tag struct {sname} vs {st.name}")
                value = self.item(r, pkg, prop, depth)
                used = (r.p - start) - (r.name_extra - extra0)
                if used != size:
                    self.stats.size_mismatch += 1
            dim = prop.data.get("array_dim", 1) if prop is not None else 1
            key = prop.name if prop is not None else name
            if dim > 1 or index:
                slot = out.setdefault(key, {})
                if not isinstance(slot, dict) or "_unknown_type" in slot:
                    slot = out[key] = {}
                slot[index] = value
            else:
                out[key] = value


# ---- class defaults --------------------------------------------------------------------------

def class_record(uni: Universe, f: Field, dec: Decoder) -> dict:
    d = f.data
    pkg = f.pkg
    rec = {
        "path": f.path,
        "super": uni.path(pkg, f.super_ref),
        "within": uni.path(pkg, d.get("within", 0)),
        "config": d.get("config_name"),
        "flags": f"0x{d.get('class_flags', 0):08x}",
        "probe_mask": f"0x{d.get('probe_mask', 0):016x}",
        "ignore_mask": f"0x{d.get('ignore_mask', 0):016x}",
        "state_flags": d.get("state_flags"),
        "script_size": d.get("script_size"),
        "script_disk": d.get("script_disk"),
        "package_imports": d.get("package_imports"),
        "dependencies": [uni.path(pkg, c) for c, _, _ in d.get("dependencies", [])],
        "hide_categories": d.get("hide_categories"),
    }
    if "error" in d:
        rec["error"] = d["error"]
        return rec
    sup = uni.field_ref(pkg, f.super_ref) if f.super_ref else None
    inherited = uni.properties(sup) if sup is not None else {}
    shadow = [c.name for c in uni.children(f) if c.kind in PROPERTY_EXTRA and c.name.lower() in inherited]
    if shadow:
        # A property declared again in this class hides the inherited one of the same name; both exist in the
        # default object, and a stored value under that name belongs to this class's declaration.
        rec["shadows"] = {n: inherited[n.lower()].path for n in shadow}
    r = pkg.export_reader(f.export)
    r.seek(d["defaults_offset"])
    rec["defaults_offset"] = d["defaults_offset"]
    try:
        rec["props"] = dec.tagged(r, pkg, f)
        if r.remaining():
            rec["error"] = f"{r.remaining()} bytes left after the defaults"
    except Exception as ex:
        rec["error"] = f"defaults: {type(ex).__name__}: {ex} at {r.p}"
    return rec


def _merge(base: Any, over: Any, prop: Optional[Field]):
    if prop is not None and prop.data.get("array_dim", 1) > 1:
        dim = prop.data["array_dim"]
        cur = list(base) if isinstance(base, list) else [None] * dim
        for i, v in over.items():
            i = int(i)
            if i < dim:
                cur[i] = v
        return cur
    if isinstance(base, dict) and isinstance(over, dict) and "enum" not in over and "X" not in over:
        merged = dict(base)
        merged.update(over)
        return merged
    return over


def effective_defaults(uni: Universe, classes: Dict[str, dict], path: str, memo: dict) -> dict:
    """Stored defaults merged down the super chain (what the class default object holds for every property
    that some class in the chain stores; properties nobody stores keep their zero value and are left out)."""
    if path in memo:
        return memo[path]
    rec = classes.get(path)
    if rec is None:
        memo[path] = {}
        return {}
    base = effective_defaults(uni, classes, rec["super"], memo) if rec.get("super") else {}
    out = dict(base)
    f = rec.get("_field")
    props = uni.properties(f) if f is not None else {}
    for k, v in (rec.get("props") or {}).items():
        out[k] = _merge(out.get(k), v, props.get(k.lower()))
    memo[path] = out
    return out


def decode_packages(names: List[str], root: Optional[Path] = None) -> Tuple[Universe, Dict[str, dict], TagStats]:
    uni = Universe(root)
    stats = TagStats()
    dec = Decoder(uni, stats)
    classes: Dict[str, dict] = {}
    for n in names:
        pkg = uni.package(n)
        if pkg is None:
            raise ClassFormatError(f"package {n} not found under {uni.root}")
        for e in pkg.exports:
            f = uni.field(pkg, e)
            if e.class_name == "Class":
                rec = class_record(uni, f, dec)
                rec["_field"] = f
                classes[f.path] = rec
    return uni, classes, stats


def structure_report(uni: Universe, names: List[str]) -> dict:
    """How many exports of each field kind parse to their last byte (Struct exports may carry defaults)."""
    c: Counter = Counter()
    bad: List[str] = []
    for n in names:
        pkg = uni.package(n)
        for e in pkg.exports:
            f = uni.fields.get((pkg.path.stem.lower(), e.index))
            if f is None or f.kind not in ("Class", "State", "Function", "Struct", "Enum", "Const") and \
                    f.kind not in PROPERTY_EXTRA:
                continue
            if "error" in f.data:
                c[f.kind + ":error"] += 1
                bad.append(f"{f.path}: {f.data['error']}")
            elif f.kind == "Class":
                c[f.kind + ":ok"] += 1
            elif f.kind == "Struct":
                left = f.data.get("size", 0) - f.data.get("end", 0)
                c["Struct:" + ("exact" if left == 0 else "with_defaults")] += 1
            elif f.data.get("end") == f.data.get("size"):
                c[f.kind + ":exact"] += 1
            else:
                c[f.kind + ":left"] += 1
                bad.append(f"{f.path}: {f.data.get('size', 0) - f.data.get('end', 0)} bytes left")
            if f.kind in ("Class", "State", "Function", "Struct") and "script_size" in f.data:
                c["script_bytes_mem"] += f.data["script_size"]
                c["script_bytes_disk"] += f.data.get("script_disk", 0)
                c["script_tokens"] += f.data.get("script_tokens", 0)
    return {"counts": dict(sorted(c.items())), "problems": bad[:200]}


def _jsonable(v):
    if isinstance(v, dict):
        return {str(k): _jsonable(x) for k, x in v.items()}
    if isinstance(v, (list, tuple)):
        return [_jsonable(x) for x in v]
    if isinstance(v, bytes):
        return v.hex()
    return v


def output_path() -> Path:
    return local_root() / "formats" / "classdefaults.json"


def load_output() -> Optional[dict]:
    p = output_path()
    if not p.exists():
        return None
    return json.loads(p.read_text(encoding="utf-8")).get("classes")


# ---- comparison with a live dump -------------------------------------------------------------

def _norm(v):
    if isinstance(v, float):
        return round(v, 4)
    if isinstance(v, dict):
        if set(v) == {"value", "enum"}:
            return v["value"]
        return {k: _norm(x) for k, x in v.items()}
    if isinstance(v, list):
        return [_norm(x) for x in v]
    return v


def _same(a, b) -> bool:
    a, b = _norm(a), _norm(b)
    if isinstance(a, float) or isinstance(b, float):
        try:
            return abs(float(a) - float(b)) <= 1e-3 * max(1.0, abs(float(a)))
        except (TypeError, ValueError):
            return False
    if isinstance(a, str) and isinstance(b, str):
        return a.lower() == b.lower() or a.split(".")[-1].lower() == b.split(".")[-1].lower()
    if isinstance(a, dict) and isinstance(b, dict):
        return all(k in b and _same(v, b[k]) for k, v in a.items())
    if isinstance(a, list) and isinstance(b, list):
        return len(a) == len(b) and all(x is None or _same(x, y) for x, y in zip(a, b))
    return a == b


def _zero(v) -> bool:
    if v in (None, 0, 0.0, False, "", "None", []):
        return True
    if isinstance(v, dict):
        if set(v) == {"value", "enum"}:
            return v["value"] == 0
        return all(_zero(x) for x in v.values())
    if isinstance(v, list):
        return all(_zero(x) for x in v)
    return False


def _shadowed_names(classes: Dict[str, dict], path: str) -> set:
    out = set()
    guard = 0
    while path in classes and guard < 64:
        guard += 1
        out.update(n.lower() for n in classes[path].get("shadows", {}))
        path = classes[path].get("super")
    return out


BOM_LE, BOM_BE = bytes([0xFF, 0xFE]), bytes([0xFE, 0xFF])
OBJECT_HEADER = {"ObjectInternal", "Outer", "ObjectFlags", "Name", "Class"}
CPF_CONFIG = 0x4000
CPF_LOCALIZED = 0x8000
CPF_GLOBALCONFIG = 0x40000


def _why(uni: "Universe", rec: Optional[dict], name: str, mine, theirs) -> str:
    """Label a disagreement: config and localized values are replaced at run time from .ini / .int files,
    and a reference into a package the process has not loaded reads as None."""
    f = rec.get("_field") if rec else None
    prop = uni.properties(f).get(name.lower()) if f is not None else None
    flags = prop.data.get("flags", 0) if prop is not None else 0
    if flags & (CPF_CONFIG | CPF_GLOBALCONFIG):
        return "config"
    if flags & CPF_LOCALIZED:
        return "localized"
    if isinstance(mine, str) and theirs is None and prop is not None and prop.kind in ("ObjectProperty", "ClassProperty"):
        top = mine.split(".")[0].lower()
        if top not in uni.pkgs or uni.pkgs[top] is None:
            return "unloaded_package_ref"
    return "other"


# The language whose text files (<Package>.int English, .fra, .deu, .esp, .ita) the converters apply. Set by svmbuild's
# --language (SVM_LANGUAGE, so worker processes get it too). A package without a file in that language uses .int.
LANGUAGES = {"int": "English", "fra": "French", "deu": "German", "esp": "Spanish", "ita": "Italian"}


def language() -> str:
    lang = os.environ.get("SVM_LANGUAGE", "int").strip().lower()
    return lang if lang in LANGUAGES else "int"


def localization_file(root: Path, package: str) -> Optional[Path]:
    """<package>.<language> in root, else <package>.int; file names are matched ignoring case (retail copies mix
    DTK09.INT and DTK09-i.int)."""
    root = Path(root)
    if not root.is_dir():
        return None
    wanted = [f"{package}.{language()}".lower(), f"{package}.int".lower()]
    by_name = _folder_names(root)
    for name in wanted:
        if name in by_name:
            return root / by_name[name]
    return None


_FOLDER_NAMES: Dict[str, Dict[str, str]] = {}


def _folder_names(root: Path) -> Dict[str, str]:
    key = str(root.resolve()).lower()
    names = _FOLDER_NAMES.get(key)
    if names is None:
        names = {p.name.lower(): p.name for p in root.iterdir() if p.is_file()}
        _FOLDER_NAMES[key] = names
    return names


def localization_files(root: Path) -> List[Path]:
    """Every text file the current language reads in root: its own files and the .int ones (the fallback)."""
    root = Path(root)
    if not root.is_dir():
        return []
    exts = {".int", "." + language()}
    return sorted((p for p in root.iterdir() if p.is_file() and p.suffix.lower() in exts), key=lambda p: p.name.lower())


def read_localization(root: Path, package: str) -> Dict[str, Dict[str, Any]]:
    """<Package>.int (or the chosen language's file): [Class] sections of Key=Value, Key[i]=Value or Key=("a","b")."""
    p = localization_file(root, package)
    return read_localization_file(p) if p is not None else {}


def read_localization_file(p: Path) -> Dict[str, Dict[str, Any]]:
    """One text file: section (lower case) -> key (lower case) -> index (None for a plain key) -> value. Struct
    values (ModesStrings=((Key=\"a\",...),(...))) are kept as their raw text; parse_struct_value reads them."""
    data = p.read_bytes()
    text = data.decode("utf-16") if data[:2] in (BOM_LE, BOM_BE) else data.decode("latin1")
    out: Dict[str, Dict[str, Any]] = {}
    sect = None
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith(";"):
            continue
        if line.startswith("[") and line.endswith("]"):
            sect = out.setdefault(line[1:-1].lower(), {})
            continue
        if sect is None or "=" not in line:
            continue
        key, val = line.split("=", 1)
        key = key.strip()
        idx = None
        for o, c in (("[", "]"), ("(", ")")):
            if key.endswith(c) and o in key:
                key, i = key[:-1].split(o, 1)
                idx = int(i) if i.strip().isdigit() else None
        sect.setdefault(key.lower(), {})[idx] = _int_value(val.strip())
    return out


def _int_value(v: str):
    if v.replace(" ", "") == "()":
        return []
    if v.startswith("((") or (v.startswith("(") and "=" in v.split('"', 1)[0]):
        return {"$struct": v}
    if v.startswith('(') and v.endswith(')') and '"' in v:
        items, cur, q = [], "", False
        for ch in v[1:-1]:
            if ch == '"':
                q = not q
                continue
            if ch == "," and not q:
                items.append(cur)
                cur = ""
                continue
            if q:
                cur += ch
        items.append(cur)
        return items
    if len(v) >= 2 and v[0] == '"' and v[-1] == '"':
        return v[1:-1]
    return v


def localized_effective(uni: "Universe", classes: Dict[str, dict], effective: Dict[str, dict]) -> Dict[str, dict]:
    """Effective defaults with the chosen language's text applied: each class's own section overrides the
    localized properties it inherits (only properties flagged CPF_Localized are taken)."""
    tables: Dict[str, dict] = {}
    memo: Dict[str, dict] = {}

    def build(path: str) -> dict:
        if path in memo:
            return memo[path]
        rec = classes.get(path)
        if rec is None:
            memo[path] = {}
            return memo[path]
        base = build(rec["super"]) if rec.get("super") else {}
        out = dict(effective.get(path, {}))
        f = rec.get("_field")
        props = uni.properties(f) if f is not None else {}
        for k, v in base.items():
            prop = props.get(k.lower())
            if prop is not None and prop.data.get("flags", 0) & CPF_LOCALIZED and k not in (rec.get("props") or {}):
                out[k] = v
        pkgname = path.split(".")[0]
        if pkgname not in tables:
            tables[pkgname] = read_localization(uni.root, pkgname)
        sect = tables[pkgname].get(path.split(".")[-1].lower(), {})
        for key, by_index in sect.items():
            prop = props.get(key)
            if prop is None or not prop.data.get("flags", 0) & CPF_LOCALIZED:
                continue
            dim = prop.data.get("array_dim", 1)
            if dim > 1:
                cur = list(out.get(prop.name) or [None] * dim)
                for i, val in by_index.items():
                    if isinstance(val, list):
                        for j, x in enumerate(val[:dim]):
                            cur[j] = x
                    elif i is not None and i < dim:
                        cur[i] = val
                out[prop.name] = cur
            else:
                val = by_index.get(None, next(iter(by_index.values())))
                out[prop.name] = val
        memo[path] = out
        return out

    return {p: build(p) for p in classes}


def compare_live(uni: "Universe", classes: Dict[str, dict], effective: Dict[str, dict], live_path: Path) -> dict:
    """Decoded defaults against a live dump of class default objects. Every property some class in the chain
    stores is compared; then live values that are not zero but that no class stores are counted (native
    constructors and .ini config values set those). Names declared twice in a chain are left out because a
    dump keyed by name shows only one of the two."""
    live = json.loads(live_path.read_text(encoding="utf-8"))
    rows = {c["path"]: c for c in live.get("classes", []) if "values" in c}
    res = Counter()
    diffs = []
    extra = []
    for path, vals in rows.items():
        if path not in effective:
            res["live_class_not_decoded"] += 1
            continue
        res["classes"] += 1
        shadowed = _shadowed_names(classes, path)
        for k, v in vals["values"].items():
            if k in OBJECT_HEADER or "." in k:
                continue
            if k not in effective[path] and not _zero(v) and k.lower() not in shadowed:
                res["live_nonzero_not_stored"] += 1
                if len(extra) < 300:
                    extra.append({"class": path, "prop": k, "live": v})
        for k, v in effective[path].items():
            if k.lower() in shadowed:
                res["shadowed_skipped"] += 1
                continue
            if k not in vals["values"]:
                res["prop_not_in_live"] += 1
                continue
            mine = v
            theirs = vals["values"][k]
            if isinstance(mine, list) and isinstance(theirs, list) and len(mine) == len(theirs):
                mine = [x if x is not None else y for x, y in zip(mine, theirs)]
            if _same(mine, theirs):
                res["agree"] += 1
            else:
                res["disagree"] += 1
                why = _why(uni, classes.get(path), k, v, theirs)
                res["disagree_" + why] += 1
                if len(diffs) < 400:
                    diffs.append({"class": path, "prop": k, "why": why, "decoded": _jsonable(v), "live": theirs})
    return {"live": str(live_path), "process": live.get("process"), "summary": dict(res), "diffs": diffs,
            "live_nonzero_not_stored": extra}


WATCH = [
    ("SBase.SWeaponDefense", "BaseDamage"), ("SBase.SWeapon", "RemoteRole"), ("Engine.Weapon", "RemoteRole"),
    ("Engine.Inventory", "RemoteRole"), ("SBase.SComputerObjectiveTrigger", "DominationCaptureTime"),
    ("SBase.SFlag", "ResetTime"), ("SBase.SComputerObjectiveTrigger", "CaptureTime"),
    ("SBase.SInterrupteur", "UsableBy"), ("SBase.SUsableObject", "UsableBy"),
    ("SBase.STriggerPlayerStart", "*"), ("SBase.SPresenceTrigger", "*"), ("Engine.LevelInfo", "GEActivationMask"),
]


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="scctformats.classdefaults", description=__doc__.split("\n")[0])
    ap.add_argument("packages", nargs="*", default=DEFAULT_PACKAGES, help="script packages (default: %(default)s)")
    ap.add_argument("--out", help="output JSON (default local/formats/classdefaults.json)")
    ap.add_argument("--live", action="append", default=[],
                    help="live class-default dump(s) to compare with (svm.reflect.cdo/1)")
    ap.add_argument("--show", action="append", default=[], help="print the stored defaults of these classes")
    args = ap.parse_args(argv)
    uni, classes, stats = decode_packages(args.packages)
    memo: dict = {}
    effective = {p: effective_defaults(uni, classes, p, memo) for p in classes}
    struct_rep = structure_report(uni, args.packages)
    out = Path(args.out) if args.out else output_path()
    out.parent.mkdir(parents=True, exist_ok=True)
    errors = {p: r["error"] for p, r in classes.items() if "error" in r}
    doc = {
        "format": "svm.classdefaults/1",
        "source": str(uni.root),
        "packages": args.packages,
        "note": "props = the defaults each class stores (only what differs from its super class); "
                "effective = props merged down the super chain. Static arrays are {index: value}.",
        "stats": {"classes": len(classes), "class_errors": len(errors), "tags": stats.tags,
                  "size_mismatch": stats.size_mismatch, "unknown": stats.unknown,
                  "type_mismatch": stats.type_mismatch, "structure": struct_rep["counts"]},
        "errors": errors,
        "notes": stats.notes[:200],
        "structure_problems": struct_rep["problems"],
        "classes": {p: _jsonable({k: v for k, v in r.items() if not k.startswith("_")}) for p, r in classes.items()},
    }
    out.write_text(json.dumps(doc, indent=1), encoding="utf-8")
    eff_path = out.with_name(out.stem + "_effective.json")
    eff_path.write_text(json.dumps(_jsonable(effective), indent=1), encoding="utf-8")
    print(json.dumps(doc["stats"], indent=1))
    for p, e in list(errors.items())[:20]:
        print("ERROR", p, e)
    for path, prop in WATCH:
        rec = classes.get(path)
        if rec is None:
            print(f"{path}: not decoded")
            continue
        if prop == "*":
            print(f"{path} stored: {json.dumps(_jsonable(rec.get('props')))[:400]}")
        else:
            print(f"{path}.{prop}: stored={json.dumps(_jsonable((rec.get('props') or {}).get(prop)))} "
                  f"effective={json.dumps(_jsonable(effective.get(path, {}).get(prop)))}")
    for s in args.show:
        rec = classes.get(s)
        print(s, json.dumps(_jsonable(rec), indent=1)[:4000] if rec else "not found")
    localized = localized_effective(uni, classes, effective)
    loc_path = out.with_name(out.stem + "_localized.json")
    loc_path.write_text(json.dumps(_jsonable(localized), indent=1), encoding="utf-8")
    for lp in args.live:
        rep = compare_live(uni, classes, effective, Path(lp))
        rep_loc = compare_live(uni, classes, localized, Path(lp))
        rep["with_int_text"] = {"summary": rep_loc["summary"], "diffs": rep_loc["diffs"]}
        cmp_path = out.with_name(out.stem + "_vs_" + Path(lp).stem + ".json")
        cmp_path.write_text(json.dumps(rep, indent=1), encoding="utf-8")
        print(f"vs {lp}: {rep['summary']}")
        print(f"   with .int text: {rep_loc['summary']} -> {cmp_path}")
    print(f"-> {out}")
    return 1 if errors else 0


if __name__ == "__main__":
    raise SystemExit(main())
