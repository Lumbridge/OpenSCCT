"""Typed property values: scctformats' exact tagged-property decoder, with references kept as references.

`scctformats.classdefaults.Decoder` reads a tagged property list against the declared properties of a class (from
the script packages), so every value has its declared type. It turns object references into dotted paths; the
subclass here keeps them as `Ref` (the raw reference, its path and whether the property is a class property), names
as `Name`, and bytes as plain integers, so the SVMap writer can tell actors, assets, classes and names apart.

`to_json` turns a decoded value into the SVMap property JSON of spec/svmap/1.1/README.md:
int/byte/enum -> integer, float -> float, bool, str/name -> string, actor -> {"$actor"}, other object -> {"$asset"},
class -> {"$class"}, struct -> object, dynamic array -> array, static array -> {"$elements"}.
"""

from __future__ import annotations

import math
import re
from dataclasses import dataclass
from typing import Any, Callable, Optional

from .env import import_formats

ASSET_RE = re.compile(r"^[^.\s]+(\.[^.\s]+)+$")
CLASS_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$")
PROP_RE = re.compile(r"^[A-Za-z_][A-Za-z0-9_]*$")


class Name(str):
    """A NameProperty value (a str that remembers it was a name)."""

    __slots__ = ()


@dataclass(frozen=True)
class Ref:
    ref: int          # raw object reference in the package that stored it (>0 export+1, <0 import, 0 None)
    path: str         # dotted path (Package.Group.Name), "" for None
    is_class: bool    # declared as a ClassProperty
    owner: str = ""   # name of the package that stored the reference (the raw ref is relative to it)


def make_decoder(universe, stats=None):
    """A TypedDecoder over a scctformats Universe."""
    cd = import_formats().classdefaults
    base = cd.Decoder

    class TypedDecoder(base):
        def item(self, r, pkg, prop, depth):
            k = prop.kind
            if k == "NameProperty":
                return Name(r.name())
            if k == "ByteProperty":
                return r.u8()
            if k in ("ObjectProperty", "ClassProperty"):
                ref = r.compact()
                return Ref(ref, self.u.path(pkg, ref) or "", k == "ClassProperty", pkg.path.stem)
            if k == "DelegateProperty":
                ref = r.compact()
                return {"object": Ref(ref, self.u.path(pkg, ref) or "", False, pkg.path.stem),
                        "function": Name(r.name())}
            return super().item(r, pkg, prop, depth)

    return TypedDecoder(universe, stats or cd.TagStats())


# A resolver maps a Ref to its JSON form ({"$actor"}, {"$asset"}, {"$class"} or None).
Resolver = Callable[[Ref], Optional[dict]]


def default_resolver(ref: Ref) -> Optional[dict]:
    """References outside a map: classes as $class, everything else as $asset."""
    if ref.ref == 0 or not ref.path:
        return None
    if ref.is_class and CLASS_RE.match(ref.path):
        return {"$class": ref.path}
    if ASSET_RE.match(ref.path):
        return {"$asset": ref.path}
    return None


class Notes:
    """Conversion notes (values that could not be written as they are), counted by kind."""

    def __init__(self) -> None:
        self.items: list[str] = []
        self.counts: dict[str, int] = {}

    def add(self, kind: str, text: str) -> None:
        self.counts[kind] = self.counts.get(kind, 0) + 1
        if len(self.items) < 200:
            self.items.append(f"{kind}: {text}")


def to_json(value: Any, resolve: Resolver, notes: Notes | None = None, where: str = "") -> Any:
    if isinstance(value, Ref):
        out = resolve(value)
        if out is None and value.ref != 0 and notes is not None:
            notes.add("unresolved-ref", f"{where} -> {value.path or value.ref}")
        return out
    if isinstance(value, bool):
        return value
    if isinstance(value, int):
        return int(value)
    if isinstance(value, float):
        if not math.isfinite(value):
            if notes is not None:
                notes.add("non-finite-float", where)
            return None
        return value
    if isinstance(value, str):
        return str(value)
    if isinstance(value, (list, tuple)):
        return [to_json(v, resolve, notes, f"{where}[{i}]") for i, v in enumerate(value)]
    if isinstance(value, dict):
        if "_unknown_type" in value:
            if notes is not None:
                notes.add("unknown-property", where)
            return None
        if value and all(isinstance(k, int) for k in value):
            # Static array: only the stored elements.
            return {"$elements": {str(k): to_json(value[k], resolve, notes, f"{where}[{k}]") for k in sorted(value)}}
        out = {}
        for k, v in value.items():
            if not isinstance(k, str) or not PROP_RE.match(k) or k.startswith("_"):
                if notes is not None:
                    notes.add("bad-member-name", f"{where}.{k}")
                continue
            out[k] = to_json(v, resolve, notes, f"{where}.{k}")
        return out
    raise TypeError(f"{where}: cannot convert {type(value).__name__}")


def bag_to_json(props: dict, resolve: Resolver, notes: Notes | None = None, where: str = "") -> dict:
    out = {}
    for k, v in props.items():
        if isinstance(v, dict) and "_unknown_type" in v:
            if notes is not None:
                notes.add("unknown-property", f"{where}.{k}")
            continue
        out[k] = to_json(v, resolve, notes, f"{where}.{k}")
    return out


def walk_refs(value: Any):
    """Every Ref inside a decoded value."""
    if isinstance(value, Ref):
        yield value
    elif isinstance(value, dict):
        for v in value.values():
            yield from walk_refs(v)
    elif isinstance(value, (list, tuple)):
        for v in value:
            yield from walk_refs(v)
