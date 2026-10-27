"""defaultproperties blocks from class default objects, parsed with scctpkg.properties."""

from __future__ import annotations

import re
from typing import Dict, List, Optional, Tuple

from .loader import Class, Enum, Property, Struct, PROPERTY_KINDS
from .pkgload import Reader, properties_module
from .printer import IND, fmt_float

_IDENT = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")
_INNER_KIND = {
    "ObjectProperty": "Object", "ClassProperty": "Class", "NameProperty": "Name", "ByteProperty": "Byte",
    "IntProperty": "Int", "FloatProperty": "Float", "StrProperty": "Str", "BoolProperty": "Bool",
}
_installed = set()


def install_array_types(u) -> int:
    """Give scctpkg's dynamic-array decoder the exact element type of every array declared in the loaded
    packages (names declared with more than one element type are left to its heuristics)."""
    if id(u) in _installed:
        return 0
    props = properties_module()
    seen: Dict[str, set] = {}
    for lp in u.packages.values():
        for f in lp.fields.values():
            if isinstance(f, Property) and f.kind == "ArrayProperty":
                inner = lp.fields.get(f.ref1)
                if not isinstance(inner, Property):
                    continue
                if inner.kind == "StructProperty":
                    t = "Struct:" + lp.obj_name(inner.ref1)
                else:
                    t = _INNER_KIND.get(inner.kind)
                if t:
                    seen.setdefault(f.name, set()).add(t)
    n = 0
    for name, ts in seen.items():
        if len(ts) == 1:
            props.ARRAY_INNER[name] = next(iter(ts))
            n += 1
        else:
            props.ARRAY_INNER.pop(name, None)
    _installed.add(id(u))
    return n


PT_DELEGATE = 7   # tag type 7 is DelegateProperty in these packages (global name 7); value = object + name


class DelegateValue(tuple):
    """(object ref, function name) stored for a delegate default."""


def read_tagged(r, depth: int = 0):
    """scctpkg.properties.read_properties with delegate tags decoded (object ref + paired name).

    Every other tag type goes through the scctpkg helpers unchanged."""
    props = properties_module()
    bag = props.PropertyBag()
    while r.remaining() > 0:
        start = r.tell()
        name = r.name()
        if name == "None":
            break
        info = r.u8()
        ptype = info & 0x0F
        size_code = (info >> 4) & 0x07
        flag = bool(info & 0x80)
        struct_name = ""
        if ptype == props.PT_STRUCT:
            struct_name = r.name()
        size = props._size_from_code(r, size_code)
        index = 0
        if flag and ptype != props.PT_BOOL:
            b = r.u8()
            if b < 128:
                index = b
            elif b & 0xC0 == 0x80:
                index = ((b & 0x7F) << 8) | r.u8()
            else:
                index = ((b & 0x3F) << 24) | (r.u8() << 16) | (r.u8() << 8) | r.u8()
        if index > 4096:
            raise props.PackageError("implausible array index %d for %s" % (index, name))
        if ptype == props.PT_BOOL:
            value = flag
        elif ptype == props.PT_BYTE:
            value = r.u8()
        elif ptype == props.PT_INT:
            value = r.i32()
        elif ptype == props.PT_FLOAT:
            value = r.f32()
        elif ptype in (props.PT_OBJECT, props.PT_CLASS):
            value = r.objref()
        elif ptype == props.PT_NAME:
            value = r.name()
        elif ptype == PT_DELEGATE:
            value = DelegateValue((r.objref(), r.name()))
        elif ptype == props.PT_STR:
            value = r.fstring()
        elif ptype == props.PT_STRUCT:
            if struct_name in props.ATOMIC_STRUCTS:
                value = props._read_atomic(r, struct_name)
            else:
                if depth > 8:
                    raise props.PackageError("struct nesting too deep")
                value = read_tagged(r, depth + 1)
                value["_struct"] = struct_name
        elif ptype == props.PT_ARRAY:
            value = props._read_dynamic_array(r, name, size, depth)
        else:
            value = r.raw(size)
        if index or (name in bag and not isinstance(bag.get(name), list)):
            existing = bag.get(name)
            if not isinstance(existing, list):
                existing = [] if existing is None else [existing]
            while len(existing) <= index:
                existing.append(None)
            existing[index] = value
            bag[name] = existing
        elif isinstance(bag.get(name), list):
            bag[name].append(value)
        else:
            bag[name] = value
        if r.tell() == start:
            raise props.PackageError("property parser made no progress")
    return bag


class DefaultsFormatter:
    def __init__(self, u, printer):
        self.u = u
        self.p = printer
        install_array_types(u)

    def read(self, c: Class):
        props = properties_module()
        r = Reader(c.raw, c.defaults_pos, c.pkg.pkg, base=c.export.offset)
        saved = props.read_properties
        props.read_properties = read_tagged      # nested struct arrays come back through here too
        try:
            bag = read_tagged(r)
        finally:
            props.read_properties = saved
        rest = len(c.raw) - r.tell()
        return bag, rest

    def format(self, c: Class) -> Tuple[List[str], Dict[str, int]]:
        stats = {"defaults_classes": 1}
        try:
            bag, rest = self.read(c)
        except Exception as ex:   # noqa: BLE001
            stats["defaults_failed"] = 1
            return ["defaultproperties", "{", IND + "// not decoded: %s" % ex, "}"], stats
        if rest:
            stats["defaults_trailing"] = 1
        out = ["defaultproperties", "{"]
        n = 0
        for sub in self.subobjects(c):
            lines, k = self.subobject(c, sub)
            out += [IND + l for l in lines]
            n += k
            stats["defaults_subobjects"] = stats.get("defaults_subobjects", 0) + 1
            if lines and lines[-1] != "End Object":
                stats["defaults_subobjects_failed"] = stats.get("defaults_subobjects_failed", 0) + 1
        n += self.entries(c, bag, out)
        out.append("}")
        stats["defaults_values"] = n
        raw = sum(1 for line in out if "elements not decoded" in line)
        if raw:
            stats["defaults_raw_arrays"] = raw
        return out, stats

    def entries(self, c: Class, bag, out: List[str], depth: int = 1) -> int:
        n = 0
        for key, value in bag.items():
            prop = self.u.find_member(c, key, PROPERTY_KINDS)
            shown = key
            if key.startswith("__") and key.endswith("__Delegate"):
                shown = key[2:-10]
            for line in self.entry(shown, value, prop):
                out.append(IND * depth + line)
                n += 1
        return n

    def subobjects(self, c: Class):
        """Objects whose outer is the class itself and that are not part of its declarations
        (GUI controls, sub-objects defined in the defaultproperties of the source)."""
        ref = c.ref
        lp = c.pkg
        for e in lp.pkg.exports:
            if e.outer == ref and e.index + 1 not in lp.fields and e.class_name not in ("TextBuffer",)                     and e.size > 0:
                yield e

    def subobject(self, c: Class, e) -> Tuple[List[str], int]:
        props = properties_module()
        lp = c.pkg
        out = ["Begin Object Class=%s Name=%s" % (e.class_name, e.name)]
        r = Reader(lp.pkg.data[e.offset:e.offset + e.size], 0, lp.pkg, base=e.offset)
        saved = props.read_properties
        props.read_properties = read_tagged
        try:
            props.skip_state_frame(r, e.flags)
            bag = read_tagged(r)
        except Exception as ex:   # noqa: BLE001
            out.append(IND + "// not decoded: %s" % ex)
            return out, 0
        finally:
            props.read_properties = saved
        cls = self.u.field(lp, e.class_ref)
        n = self.entries(cls, bag, out) if isinstance(cls, Class) else 0
        out.append("End Object")
        return out, n

    # values -------------------------------------------------------------------------------------
    def entry(self, key: str, value, prop: Optional[Property]) -> List[str]:
        if isinstance(value, list):
            elem = prop
            if prop is not None and prop.kind == "ArrayProperty":
                elem = prop.pkg.fields.get(prop.ref1)
            out = []
            for i, v in enumerate(value):
                if v is None:
                    continue
                out.append("%s(%d)=%s" % (key, i, self.value(v, elem)))
            if not out and prop is not None and prop.kind == "ArrayProperty":
                out.append("%s=()" % key)
            return out
        if isinstance(value, dict) and "_raw" in value:
            inner = prop.pkg.fields.get(prop.ref1) if isinstance(prop, Property) and prop.kind == "ArrayProperty" else None
            if isinstance(inner, Property) and inner.kind == "BoolProperty" and len(value["_raw"]) == value["_count"]:
                return ["%s(%d)=%s" % (key, i, "True" if b else "False") for i, b in enumerate(value["_raw"])]
            return ["// %s: %d elements not decoded (%d bytes)" % (key, value["_count"], len(value["_raw"]))]
        return ["%s=%s" % (key, self.value(value, prop))]

    def value(self, v, prop: Optional[Property]) -> str:
        kind = prop.kind if isinstance(prop, Property) else None
        if isinstance(v, DelegateValue):
            return v[1]
        if isinstance(v, bool):
            return "True" if v else "False"
        if kind == "ByteProperty" and isinstance(v, int):
            en = self.u.field(prop.pkg, prop.ref1) if prop.ref1 else None
            if isinstance(en, Enum) and 0 <= v < len(en.values):
                return en.values[v]
            return str(v)
        if kind in ("ObjectProperty", "ClassProperty") and isinstance(v, int):
            return self.object_text(prop, v)
        if isinstance(v, float):
            return fmt_float(v)
        if isinstance(v, int):
            return str(v)
        if isinstance(v, str):
            if kind == "NameProperty":
                return v if _IDENT.match(v) else '"%s"' % v
            return '"%s"' % v.replace("\\", "\\\\").replace('"', '\\"')
        if isinstance(v, (tuple, list)):
            sname = prop.pkg.obj_name(prop.ref1) if kind == "StructProperty" else ""
            names = {"Vector": ("X", "Y", "Z"), "Rotator": ("Pitch", "Yaw", "Roll"),
                     "Color": ("R", "G", "B", "A")}.get(sname)
            if names is None:
                names = ("X", "Y", "Z", "W")[:len(v)] if len(v) <= 4 else tuple("V%d" % i for i in range(len(v)))
            parts = []
            for nmv, x in zip(names, v):
                parts.append("%s=%s" % (nmv, fmt_float(x) if isinstance(x, float) else x))
            return "(" + ",".join(parts) + ")"
        if isinstance(v, dict):
            st = None
            if kind == "StructProperty":
                st = self.u.field(prop.pkg, prop.ref1)
            parts = []
            for k, x in v.items():
                if k == "_struct":
                    continue
                sub = self.u.find_member(st, k, PROPERTY_KINDS) if isinstance(st, Struct) else None
                if isinstance(x, list):
                    elem = sub.pkg.fields.get(sub.ref1) if isinstance(sub, Property) and sub.kind == "ArrayProperty" else sub
                    for i, y in enumerate(x):
                        if y is not None:
                            parts.append("%s(%d)=%s" % (k, i, self.value(y, elem)))
                else:
                    parts.append("%s=%s" % (k, self.value(x, sub)))
            return "(" + ",".join(parts) + ")"
        if isinstance(v, (bytes, bytearray)):
            return "/* raw %s */" % bytes(v).hex()
        return "/* %r */" % (v,)

    def object_text(self, prop: Property, ref: int) -> str:
        if ref == 0:
            return "None"
        lp = self.p.lp
        cls = lp.class_of_ref(ref) or "Object"
        path = lp.path_of(ref)
        if cls == "Class":
            return "Class'%s'" % path
        return "%s'%s'" % (cls, path)
