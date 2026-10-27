"""Loader for the v300 UField hierarchy: UStruct, UFunction, UState, UClass, properties, enums, consts.

Export layouts (file form, verified by consuming every export of the five packages to its exact size):

    UObject      tagged property list ending in None (absent for UClass exports)
    UField       SuperField obj, Next obj
    UStruct      ScriptText obj, Children obj, FriendlyName name, CppText obj, Line i32, TextPos i32,
                 ScriptSize i32 (in-memory bytes), script tokens
    UFunction    UStruct, iNative u16, OperPrecedence u8, FunctionFlags u32, RepOffset u16 if FUNC_Net
    UState       UStruct, ProbeMask u64, IgnoreMask u64, LabelTableOffset u16, StateFlags u32
    UClass       UState, ClassFlags u32, ClassGuid 16 bytes, Dependencies, PackageImports, ClassWithin
                 obj, ClassConfigName name, HideCategories, then the class defaults (tagged list)
    UProperty    UField, ArrayDim i32, PropertyFlags u32, Category name, RepOffset u16 if CPF_Net,
                 then the subclass reference (Enum, PropertyClass, MetaClass, Struct, Inner, Function)
    UEnum        UField, names array
    UConst       UField, Value string
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

from . import bytecode as B
from .pkgload import Package, Reader, PackageError

# Function flags (UE2 numbering).
FUNC_Final = 0x00000001
FUNC_Defined = 0x00000002
FUNC_Iterator = 0x00000004
FUNC_Latent = 0x00000008
FUNC_PreOperator = 0x00000010
FUNC_Singular = 0x00000020
FUNC_Net = 0x00000040
FUNC_NetReliable = 0x00000080
FUNC_Simulated = 0x00000100
FUNC_Exec = 0x00000200
FUNC_Native = 0x00000400
FUNC_Event = 0x00000800
FUNC_Operator = 0x00001000
FUNC_Static = 0x00002000
FUNC_NoExport = 0x00004000
FUNC_Const = 0x00008000
FUNC_Invariant = 0x00010000
FUNC_Public = 0x00020000
FUNC_Private = 0x00040000
FUNC_Protected = 0x00080000
FUNC_Delegate = 0x00100000

FUNC_FLAG_NAMES = [
    (FUNC_Final, "Final"), (FUNC_Defined, "Defined"), (FUNC_Iterator, "Iterator"), (FUNC_Latent, "Latent"),
    (FUNC_PreOperator, "PreOperator"), (FUNC_Singular, "Singular"), (FUNC_Net, "Net"),
    (FUNC_NetReliable, "NetReliable"), (FUNC_Simulated, "Simulated"), (FUNC_Exec, "Exec"),
    (FUNC_Native, "Native"), (FUNC_Event, "Event"), (FUNC_Operator, "Operator"), (FUNC_Static, "Static"),
    (FUNC_NoExport, "NoExport"), (FUNC_Const, "Const"), (FUNC_Invariant, "Invariant"), (FUNC_Public, "Public"),
    (FUNC_Private, "Private"), (FUNC_Protected, "Protected"), (FUNC_Delegate, "Delegate"),
]

# Property flags.
CPF_Edit = 0x00000001
CPF_Const = 0x00000002
CPF_Input = 0x00000004
CPF_ExportObject = 0x00000008
CPF_OptionalParm = 0x00000010
CPF_Net = 0x00000020
CPF_EditConstArray = 0x00000040
CPF_Parm = 0x00000080
CPF_OutParm = 0x00000100
CPF_SkipParm = 0x00000200
CPF_ReturnParm = 0x00000400
CPF_CoerceParm = 0x00000800
CPF_Native = 0x00001000
CPF_Transient = 0x00002000
CPF_Config = 0x00004000
CPF_Localized = 0x00008000
CPF_Travel = 0x00010000
CPF_EditConst = 0x00020000
CPF_GlobalConfig = 0x00040000
CPF_OnDemand = 0x00100000
CPF_New = 0x00200000
CPF_NeedCtorLink = 0x00400000
CPF_NoExport = 0x00800000
CPF_Button = 0x01000000
CPF_EditInline = 0x04000000
CPF_EdFindable = 0x08000000
CPF_EditInlineUse = 0x10000000
CPF_Deprecated = 0x20000000
CPF_EditInlineNotify = 0x40000000

CPF_FLAG_NAMES = [
    (CPF_Edit, "Edit"), (CPF_Const, "Const"), (CPF_Input, "Input"), (CPF_ExportObject, "ExportObject"),
    (CPF_OptionalParm, "OptionalParm"), (CPF_Net, "Net"), (CPF_EditConstArray, "EditConstArray"),
    (CPF_Parm, "Parm"), (CPF_OutParm, "OutParm"), (CPF_SkipParm, "SkipParm"), (CPF_ReturnParm, "ReturnParm"),
    (CPF_CoerceParm, "CoerceParm"), (CPF_Native, "Native"), (CPF_Transient, "Transient"), (CPF_Config, "Config"),
    (CPF_Localized, "Localized"), (CPF_Travel, "Travel"), (CPF_EditConst, "EditConst"),
    (CPF_GlobalConfig, "GlobalConfig"), (0x00080000, "Flag80000"), (CPF_OnDemand, "OnDemand"), (CPF_New, "New"),
    (CPF_NeedCtorLink, "NeedCtorLink"), (CPF_NoExport, "NoExport"), (CPF_Button, "Button"),
    (0x02000000, "Flag2000000"), (CPF_EditInline, "EditInline"), (CPF_EdFindable, "EdFindable"),
    (CPF_EditInlineUse, "EditInlineUse"), (CPF_Deprecated, "Deprecated"), (CPF_EditInlineNotify, "EditInlineNotify"),
    (0x80000000, "Flag80000000"),
]

# Class flags.
CLASS_FLAG_NAMES = [
    (0x00000001, "Abstract"), (0x00000002, "Compiled"), (0x00000004, "Config"), (0x00000008, "Transient"),
    (0x00000010, "Parsed"), (0x00000020, "Localized"), (0x00000040, "SafeReplace"), (0x00000080, "RuntimeStatic"),
    (0x00000100, "NoExport"), (0x00000200, "Placeable"), (0x00000400, "PerObjectConfig"),
    (0x00000800, "NativeReplication"), (0x00001000, "EditInlineNew"), (0x00002000, "CollapseCategories"),
    (0x00004000, "ExportStructs"), (0x00008000, "Flag8000"), (0x00010000, "Flag10000"), (0x00020000, "Flag20000"),
    (0x00040000, "Flag40000"), (0x00080000, "Flag80000"), (0x00100000, "Flag100000"), (0x00200000, "Flag200000"),
]

STATE_FLAG_NAMES = [(0x1, "Editable"), (0x2, "Auto"), (0x4, "Simulated")]

PROPERTY_KINDS = {
    "ByteProperty", "IntProperty", "BoolProperty", "FloatProperty", "ObjectProperty", "NameProperty",
    "StringProperty", "ClassProperty", "ArrayProperty", "StructProperty", "StrProperty", "MapProperty",
    "FixedArrayProperty", "DelegateProperty", "PointerProperty",
}
STRUCT_KINDS = {"Function", "State", "Class", "Struct"}


def flag_names(value: int, table) -> List[str]:
    out = [n for b, n in table if value & b]
    rest = value
    for b, _ in table:
        rest &= ~b
    if rest:
        out.append("0x%X" % rest)
    return out


@dataclass
class Field:
    pkg: "LoadedPackage"
    ref: int            # export index + 1
    kind: str           # class name of the export (Function, IntProperty, ...)
    name: str
    outer: int
    super_field: int = 0
    next: int = 0

    @property
    def export(self):
        return self.pkg.pkg.exports[self.ref - 1]

    @property
    def path(self) -> str:
        return self.pkg.path_of(self.ref)


@dataclass
class Property(Field):
    array_dim: int = 1
    flags: int = 0
    category: str = "None"
    rep_offset: int = -1
    ref1: int = 0       # Enum / PropertyClass / Struct / Inner / Function / Key
    ref2: int = 0       # MetaClass / Value
    fixed_count: int = 0


@dataclass
class Struct(Field):
    script_text: int = 0
    children: int = 0
    friendly_name: str = "None"
    cpp_text: int = 0
    line: int = 0
    text_pos: int = 0
    script_size: int = 0
    script_pos: int = 0     # offset of the first token inside the export data
    script_end: int = 0     # file offset (inside the export data) just past the last token
    script: List[B.Expr] = field(default_factory=list)
    script_error: str = ""
    fields: List[int] = field(default_factory=list)   # Children chain (refs)
    raw: bytes = b""

    @property
    def script_bytes(self) -> bytes:
        return self.raw[self.script_pos:self.script_end]


@dataclass
class Function(Struct):
    native: int = 0
    oper_prec: int = 0
    func_flags: int = 0
    rep_offset: int = -1


@dataclass
class State(Struct):
    probe_mask: int = 0
    ignore_mask: int = 0
    label_table_offset: int = 0xFFFF
    state_flags: int = 0


@dataclass
class Class(State):
    class_flags: int = 0
    guid: bytes = b""
    dependencies: List[Tuple[int, int, int]] = field(default_factory=list)
    package_imports: List[str] = field(default_factory=list)
    within: int = 0
    config_name: str = "None"
    hide_categories: List[str] = field(default_factory=list)
    defaults_pos: int = 0


@dataclass
class Enum(Field):
    values: List[str] = field(default_factory=list)


@dataclass
class Const(Field):
    value: str = ""


class LoadError(Exception):
    pass


class LoadedPackage:
    """One .u package with every UField export parsed."""

    def __init__(self, path: str):
        self.pkg = Package.load(path)
        self.name = self.pkg.path.stem
        self.fields: Dict[int, Field] = {}
        self.errors: Dict[int, str] = {}
        none = [i for i, n in enumerate(self.pkg.names) if n == "None"]
        self.none_index = set(none)
        self.global_name: Dict[int, int] = {}   # local name index -> cooked global name index
        self._read_export_name_globals()
        for e in self.pkg.exports:
            try:
                f = self._load(e)
            except (PackageError, B.DecodeError, IndexError, struct.error, LoadError) as ex:
                self.errors[e.index + 1] = "%s: %s" % (type(ex).__name__, ex)
                continue
            if f is not None:
                self.fields[e.index + 1] = f
        for f in self.fields.values():
            if isinstance(f, Struct):
                f.fields = self._chain(f.children)

    def _read_export_name_globals(self):
        """Re-walk the import and export tables keeping the cooked global index of every name.

        The global index is the engine's FName index at cook time; for hard-coded names (probe
        functions among them) it equals the engine's fixed name number."""
        d = self.pkg.data
        nc, no, ec, eo, ic, io = struct.unpack_from("<6i", d, 12)
        p = io
        for _ in range(ic):
            for k in range(4):
                if k == 2:
                    p += 4
                    continue
                a, p = B.read_compact(d, p)
                g, p = B.read_compact(d, p)
                self.global_name.setdefault(a, g)
        p = eo
        for _ in range(ec):
            _, p = B.read_compact(d, p)
            _, p = B.read_compact(d, p)
            p += 4
            a, p = B.read_compact(d, p)
            g, p = B.read_compact(d, p)
            self.global_name.setdefault(a, g)
            p += 4
            size, p = B.read_compact(d, p)
            if size > 0:
                _, p = B.read_compact(d, p)

    # names and paths ----------------------------------------------------------------------------
    def name_of(self, idx: int) -> str:
        return self.pkg.names[idx] if 0 <= idx < len(self.pkg.names) else "?name%d" % idx

    def obj_name(self, ref: int) -> str:
        return self.pkg.object_name(ref) if ref else "None"

    def path_of(self, ref: int) -> str:
        """Package-qualified dotted path of an object reference (imports keep their package)."""
        parts = []
        guard = 0
        while ref and guard < 32:
            guard += 1
            if ref > 0:
                e = self.pkg.exports[ref - 1]
                parts.append(e.name)
                ref = e.outer
                if ref == 0:
                    parts.append(self.name)
            else:
                i = self.pkg.imports[-ref - 1]
                parts.append(i.name)
                ref = i.outer
        return ".".join(reversed(parts))

    def class_of_ref(self, ref: int) -> str:
        if ref > 0:
            return self.pkg.exports[ref - 1].class_name
        if ref < 0:
            return self.pkg.imports[-ref - 1].class_name
        return ""

    def _chain(self, ref: int) -> List[int]:
        out, seen = [], set()
        while ref > 0 and ref not in seen:
            seen.add(ref)
            out.append(ref)
            f = self.fields.get(ref)
            if f is None:
                break
            ref = f.next
        return out

    # parsing ------------------------------------------------------------------------------------
    def _load(self, e) -> Optional[Field]:
        kind = e.class_name
        if kind not in STRUCT_KINDS and kind not in PROPERTY_KINDS and kind not in ("Enum", "Const"):
            return None
        if e.size <= 0:
            raise LoadError("empty export")
        raw = self.pkg.data[e.offset:e.offset + e.size]
        r = Reader(raw, 0, self.pkg, base=e.offset)
        if kind != "Class":
            if e.flags & 0x02000000:
                raise LoadError("unexpected RF_HasStack")
            nm = r.name()
            if nm != "None":
                raise LoadError("unexpected tagged property %s on a %s" % (nm, kind))
        common = dict(pkg=self, ref=e.index + 1, kind=kind, name=e.name, outer=e.outer)
        sup = r.compact()
        nxt = r.compact()
        common.update(super_field=sup, next=nxt)
        if kind in PROPERTY_KINDS:
            f = Property(**common)
            f.array_dim = r.i32()
            f.flags = r.u32()
            f.category = r.name()
            if f.flags & CPF_Net:
                f.rep_offset = r.u16()
            if kind in ("ByteProperty", "ObjectProperty", "StructProperty", "ArrayProperty", "DelegateProperty",
                        "PointerProperty"):
                f.ref1 = r.compact()
            elif kind == "ClassProperty":
                f.ref1 = r.compact()
                f.ref2 = r.compact()
            elif kind == "MapProperty":
                f.ref1 = r.compact()
                f.ref2 = r.compact()
            elif kind == "FixedArrayProperty":
                f.ref1 = r.compact()
                f.fixed_count = r.i32()
            self._expect_end(r, raw, kind)
            return f
        if kind == "Enum":
            f = Enum(**common)
            f.values = [r.name() for _ in range(r.compact())]
            self._expect_end(r, raw, kind)
            return f
        if kind == "Const":
            f = Const(**common)
            f.value = r.fstring()
            self._expect_end(r, raw, kind)
            return f
        cls = {"Function": Function, "State": State, "Class": Class, "Struct": Struct}[kind]
        f = cls(**common)
        f.raw = raw
        f.script_text = r.compact()
        f.children = r.compact()
        f.friendly_name = r.name()
        f.cpp_text = r.compact()
        f.line = r.i32()
        f.text_pos = r.i32()
        f.script_size = r.i32()
        f.script_pos = r.tell()
        if f.script_size < 0 or f.script_size > 1 << 20:
            raise LoadError("bad ScriptSize %d" % f.script_size)
        try:
            f.script, end = B.decode_script(raw, f.script_pos, f.script_size, lambda i: i in self.none_index)
        except B.DecodeError as ex:
            f.script_error = str(ex)
            f.script_end = f.script_pos
            if kind == "Struct":
                raise
            # The fixed-size tail of functions lets us continue past an undecodable script.
            return f
        f.script_end = end
        r.seek(end)
        if kind == "Function":
            f.native = r.u16()
            f.oper_prec = r.u8()
            f.func_flags = r.u32()
            if f.func_flags & FUNC_Net:
                f.rep_offset = r.u16()
            self._expect_end(r, raw, kind)
        elif kind in ("State", "Class"):
            f.probe_mask = struct.unpack_from("<Q", raw, r.tell())[0]
            f.ignore_mask = struct.unpack_from("<Q", raw, r.tell() + 8)[0]
            r.seek(r.tell() + 16)
            f.label_table_offset = r.u16()
            f.state_flags = r.u32()
            if kind == "State":
                self._expect_end(r, raw, kind)
            else:
                f.class_flags = r.u32()
                f.guid = r.raw(16)
                f.dependencies = []
                for _ in range(r.compact()):
                    f.dependencies.append((r.compact(), r.u32(), r.u32()))
                f.package_imports = [r.name() for _ in range(r.compact())]
                f.within = r.compact()
                f.config_name = r.name()
                f.hide_categories = [r.name() for _ in range(r.compact())]
                f.defaults_pos = r.tell()
        else:
            self._expect_end(r, raw, kind)
        return f

    @staticmethod
    def _expect_end(r: Reader, raw: bytes, kind: str):
        if r.tell() != len(raw):
            raise LoadError("%s: %d trailing bytes" % (kind, len(raw) - r.tell()))

    # queries ------------------------------------------------------------------------------------
    def of_kind(self, *kinds) -> List[Field]:
        return [f for f in self.fields.values() if f.kind in kinds]

    def classes(self) -> List[Class]:
        return [f for f in self.fields.values() if isinstance(f, Class)]
