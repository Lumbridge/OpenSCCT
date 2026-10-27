"""Tagged property list parser (UnrealScript default/instance properties).

Layout per tag (UE2, with the SCCT paired-name quirk):

    name      : paired name ref (local compact, global compact); "None" ends the list
    info      : u8, bits 0-3 type, bits 4-6 size code, bit 7 array flag (or bool value)
    [struct]  : paired name ref of the struct type, only for StructProperty
    size      : code 0..4 => 1,2,4,12,16 bytes; 5 => u8, 6 => u16, 7 => u32 follows
    [index]   : array element index when the array flag is set (not for bools)
    value

Quirks verified on this game's packages:

* Atomic structs (Vector, Rotator, Color, Plane, Scale, Coords, Box, Quat)
  are stored natively. Other structs are nested tagged lists and their size
  field can be wrong, so they are parsed by tags until the None terminator.
* Bool tags with size code 5 still carry a size byte (0) after the info byte.
* Object and Name values are compact / paired-name encoded regardless of the
  size code, so they are decoded structurally rather than by byte count.
"""

from __future__ import annotations

import json
import os
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

from .package import PackageError, Reader

# Property type ids (UE2 EPropertyType).
PT_BYTE = 1
PT_INT = 2
PT_BOOL = 3
PT_FLOAT = 4
PT_OBJECT = 5
PT_NAME = 6
PT_STRING = 7
PT_CLASS = 8
PT_ARRAY = 9
PT_STRUCT = 10
PT_VECTOR = 11
PT_ROTATOR = 12
PT_STR = 13
PT_MAP = 14
PT_FIXEDARRAY = 15

ATOMIC_STRUCTS = {
    # Only these three are stored natively in this game; every other struct
    # (Scale, Plane, PointRegion, Range ...) is a nested tagged list.
    "Vector": ("vec3", 12),
    "Rotator": ("rot", 12),
    "Color": ("color", 4),
}


@dataclass
class Prop:
    name: str
    type: int
    struct: str = ""
    index: int = 0
    value: Any = None
    raw: bytes = b""


class PropertyBag(dict):
    """dict name -> value; arrays keep index order in a list."""

    def get_vec(self, name: str, default=(0.0, 0.0, 0.0)):
        v = self.get(name)
        return tuple(v) if v is not None else default

    def get_rot(self, name: str, default=(0, 0, 0)):
        v = self.get(name)
        return tuple(v) if v is not None else default


def _size_from_code(r: Reader, code: int) -> int:
    if code == 0:
        return 1
    if code == 1:
        return 2
    if code == 2:
        return 4
    if code == 3:
        return 12
    if code == 4:
        return 16
    if code == 5:
        return r.u8()
    if code == 6:
        return r.u16()
    return r.u32()


def _read_atomic(r: Reader, struct_name: str):
    kind, _ = ATOMIC_STRUCTS[struct_name]
    if kind == "vec3":
        return r.vec3()
    if kind == "rot":
        return r.rot()
    if kind == "color":
        b, g, rr, a = r.raw(4)  # stored BGRA
        return (rr, g, b, a)
    if kind == "plane":
        return (r.f32(), r.f32(), r.f32(), r.f32())
    if kind == "scale":
        return {"Scale": r.vec3(), "SheerRate": r.f32(), "SheerAxis": r.u8(), "_pad": r.raw(3)}
    if kind == "coords":
        return {"Origin": r.vec3(), "XAxis": r.vec3(), "YAxis": r.vec3(), "ZAxis": r.vec3()}
    if kind == "box":
        return {"Min": r.vec3(), "Max": r.vec3(), "IsValid": r.u8()}
    if kind == "quat":
        return (r.f32(), r.f32(), r.f32(), r.f32())
    if kind == "intbox":
        return (r.i32(), r.i32(), r.i32(), r.i32())
    if kind == "range":
        return (r.f32(), r.f32())
    if kind == "rangevec":
        return {"X": (r.f32(), r.f32()), "Y": (r.f32(), r.f32()), "Z": (r.f32(), r.f32())}
    raise PackageError(f"unhandled atomic {struct_name}")


def _peek_tag_valid(r: Reader) -> bool:
    """True when a plausible property tag (or None terminator) starts here."""
    if r.remaining() == 0:
        return True
    save = r.tell()
    extra = r.name_extra   # a peek must not count the name it reads (it broke nested struct-array size checks)
    try:
        name = r.name()
        if name == "None":
            return True
        info = r.u8()
        ptype = info & 0x0F
        return 1 <= ptype <= 15 and r.remaining() >= 0
    except Exception:
        return False
    finally:
        r.seek(save)
        r.name_extra = extra


# Element types of dynamic array properties, read from the running game's reflection (ArrayProperty.Inner) for
# every array name used by the game's classes. Names declared with two different types ('Materials') keep the
# heuristic. Without them an object array whose byte count happened to fit was decoded as a paired-name array,
# swallowed the next tag and turned whole actors (doors, lifts, gameplay props) into empty markers.
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "array_inner_types.json"), encoding="utf-8") as _f:
    ARRAY_INNER = {k: v[0] for k, v in json.load(_f).items() if len(v) == 1}


def _read_typed_array(r: Reader, name: str, inner: str, size: int, depth: int):
    """Decode a dynamic array by its known element type; None (reader rewound) when the result does not fit."""
    start, extra0 = r.tell(), r.name_extra
    try:
        count = r.compact()
        if count < 0 or count > r.remaining():
            raise PackageError("count")
        if inner in ("Object", "Class"):
            v = [r.objref() for _ in range(count)]
        elif inner == "Name":
            v = [r.name() for _ in range(count)]
        elif inner == "Byte":
            v = list(r.raw(count))
        elif inner == "Int":
            v = [r.i32() for _ in range(count)]
        elif inner == "Float":
            v = [r.f32() for _ in range(count)]
        elif inner == "Str":
            v = [r.fstring() for _ in range(count)]
        elif inner.startswith("Struct:") and inner[7:] in ATOMIC_STRUCTS:
            v = [_read_atomic(r, inner[7:]) for _ in range(count)]
        elif inner.startswith("Struct:") and depth < 8:
            v = []
            for _ in range(count):
                b = read_properties(r, depth + 1)
                b["_struct"] = inner[7:]
                v.append(b)
        else:
            raise PackageError("type")
        diff = (r.tell() - start) - (r.name_extra - extra0)
        # Tagged struct elements: the tag size leaves out each element's None terminator (SLift.DoorsAndSwitches).
        if (diff == size or (inner.startswith("Struct:") and diff == size + count)) and _peek_tag_valid(r):
            return v
    except Exception:
        pass
    r.seek(start)
    r.name_extra = extra0
    return None


def _read_dynamic_array(r: Reader, name: str, size: int, depth: int):
    """Dynamic arrays: compact count, then elements. Arrays with a known element type (ARRAY_INNER) are decoded
    by type first. Otherwise the tag size, which excludes the global-name compacts, picks the decoding: a
    candidate is accepted when (bytes consumed - global-name bytes) == size."""
    inner = ARRAY_INNER.get(name)
    if inner:
        v = _read_typed_array(r, name, inner, size, depth)
        if v is not None:
            return v
    start = r.tell()
    extra0 = r.name_extra
    count = r.compact()
    count_len = r.tell() - start
    if count == 0:
        return []
    if count < 0 or count > r.remaining():
        raise PackageError(f"implausible array count {count} for {name}")

    def matches() -> bool:
        return (r.tell() - start) - (r.name_extra - extra0) == size

    # 1. tagged struct elements
    if depth < 8:
        try:
            elems = []
            for _ in range(count):
                elems.append(read_properties(r, depth + 1))
            if matches():
                return elems
        except Exception:
            pass
    # 2. paired name elements
    r.seek(start + count_len)
    r.name_extra = extra0
    try:
        elems = [r.name() for _ in range(count)]
        if matches():
            return elems
    except Exception:
        pass
    # 3. elements without names: exact size
    r.seek(start + count_len)
    r.name_extra = extra0
    plain_len = size - count_len
    if plain_len < 0 or plain_len > r.remaining():
        raise PackageError(f"cannot parse dynamic array {name} (count {count}, size {size})")
    raw = r.raw(plain_len)
    return {"_count": count, "_raw": raw}


TRACE = None  # optional callable(depth, offset, name, ptype, struct_name, size) for debugging


def read_properties(r: Reader, depth: int = 0) -> PropertyBag:
    """Parse a tagged property list at the reader position until None."""
    bag = PropertyBag()
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
        if ptype == PT_STRUCT:
            struct_name = r.name()
        size = _size_from_code(r, size_code)
        index = 0
        if flag and ptype != PT_BOOL:
            # array index: compact-like encoding (UT2004 UnClass.cpp)
            b = r.u8()
            if b < 128:
                index = b
            elif b & 0xC0 == 0x80:
                index = ((b & 0x7F) << 8) | r.u8()
            else:
                index = ((b & 0x3F) << 24) | (r.u8() << 16) | (r.u8() << 8) | r.u8()
        if index > 4096:
            raise PackageError(f"implausible array index {index} for {name}")
        if TRACE:
            TRACE(depth, start, name, ptype, struct_name, size)
        value_start = r.tell()
        value: Any
        if ptype == PT_BOOL:
            value = flag
        elif ptype == PT_BYTE:
            value = r.u8()
        elif ptype == PT_INT:
            value = r.i32()
        elif ptype == PT_FLOAT:
            value = r.f32()
        elif ptype in (PT_OBJECT, PT_CLASS):
            value = r.objref()
        elif ptype == PT_NAME:
            value = r.name()
        elif ptype in (PT_STR, PT_STRING):
            value = r.fstring()
        elif ptype == PT_STRUCT:
            if struct_name in ATOMIC_STRUCTS:
                value = _read_atomic(r, struct_name)
            else:
                if depth > 8:
                    raise PackageError("struct nesting too deep")
                value = read_properties(r, depth + 1)
                value["_struct"] = struct_name
        elif ptype == PT_ARRAY:
            value = _read_dynamic_array(r, name, size, depth)
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
            raise PackageError("property parser made no progress")
    return bag


def skip_state_frame(r: Reader, flags: int) -> None:
    """Actors and other stateful objects (RF_HasStack = 0x02000000) begin with
    an FStateFrame: Node objref, StateNode objref, ProbeMask int64,
    LatentAction int32, then a compact Offset when Node is set."""
    if not (flags & 0x02000000):
        return
    node = r.compact()
    r.compact()      # StateNode
    r.i64()          # ProbeMask
    r.i32()          # LatentAction
    if node != 0:
        r.compact()  # Offset
