"""Exact class defaults of the game's script packages, with typed values.

Built on scctformats.classdefaults: its Universe reads every class, property, struct and enum export of
System/_PC_/*.u, its class_record reads a class's stored defaults, effective_defaults merges them down the super
chain and localized_effective applies the English .int text. The only difference is the value decoder: the typed one
from typedprops, so references stay references.

Merging rules (UE2): a subclass stores only what differs from its super class; a stored struct overlays the inherited
value member by member; a stored static array element replaces that element; a dynamic array replaces the whole value.
"""

from __future__ import annotations

from functools import cached_property, lru_cache
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional

from .env import GAME_SCRIPT_PACKAGES, import_formats
from .typedprops import Name, Notes, Ref, default_resolver, make_decoder, to_json

PROPERTY_TYPES = {
    "ByteProperty": "byte", "IntProperty": "int", "BoolProperty": "bool", "FloatProperty": "float",
    "ObjectProperty": "object", "ClassProperty": "class", "NameProperty": "name", "StrProperty": "string",
    "StringProperty": "string", "ArrayProperty": "array", "FixedArrayProperty": "fixedarray",
    "StructProperty": "struct", "MapProperty": "map", "DelegateProperty": "delegate", "PointerProperty": "pointer",
}


class ClassDB:
    def __init__(self, game: Path, packages: Iterable[str] = GAME_SCRIPT_PACKAGES):
        cd = import_formats().classdefaults
        self.cd = cd
        self.root = Path(game) / "System" / "_PC_"
        self.packages = list(packages)
        self.universe = cd.Universe(self.root)
        self.decoder = make_decoder(self.universe)
        classes: Dict[str, dict] = {}
        for n in self.packages:
            pkg = self.universe.package(n)
            if pkg is None:
                raise FileNotFoundError(f"script package {n}.u not found under {self.root}")
            for e in pkg.exports:
                f = self.universe.field(pkg, e)
                if e.class_name == "Class":
                    rec = cd.class_record(self.universe, f, self.decoder)
                    rec["_field"] = f
                    classes[f.path] = rec
        self.classes = classes
        memo: dict = {}
        self.effective = {p: cd.effective_defaults(self.universe, classes, p, memo) for p in classes}
        self.localized = cd.localized_effective(self.universe, classes, self.effective)
        self._lower = {p.lower(): p for p in classes}
        self.errors = {p: r["error"] for p, r in classes.items() if "error" in r}

    # ---- lookup --------------------------------------------------------------------------------

    def find(self, path: str) -> Optional[str]:
        """Canonical spelling of a class path (Package.Class), ignoring case."""
        return self._lower.get(path.lower())

    def record(self, path: str) -> Optional[dict]:
        p = self.find(path)
        return self.classes.get(p) if p else None

    def field(self, path: str):
        rec = self.record(path)
        return rec["_field"] if rec else None

    def chain(self, path: str) -> List[str]:
        """The class and its super classes, most derived first."""
        out = []
        p = self.find(path)
        guard = 0
        while p and guard < 64:
            guard += 1
            out.append(p)
            sup = self.classes[p].get("super")
            p = self.find(sup) if sup else None
        return out

    def is_a(self, path: str, base: str) -> bool:
        b = base.lower()
        return any(c.lower() == b for c in self.chain(path))

    def prop(self, path: str, name: str):
        f = self.field(path)
        if f is None:
            return None
        return self.universe.properties(f).get(name.lower())

    def default(self, path: str, name: str, default: Any = None) -> Any:
        """Effective (localized) default of one property, as decoded (Ref / Name / int / ...)."""
        p = self.find(path)
        if p is None:
            return default
        return self.localized.get(p, {}).get(name, default)

    def value(self, path: str, props: dict, name: str, default: Any = None) -> Any:
        """Effective value of a property of an instance: its stored value, else the class default."""
        if name in props:
            return props[name]
        return self.default(path, name, default)

    # ---- typed JSON ----------------------------------------------------------------------------

    def defaults_json(self, path: str, resolve=default_resolver, notes: Notes | None = None) -> dict:
        """Effective defaults of a class as SVMap property JSON (static arrays as their set elements)."""
        p = self.find(path)
        if p is None:
            return {}
        f = self.classes[p]["_field"]
        props = self.universe.properties(f)
        out = {}
        for k, v in self.localized.get(p, {}).items():
            prop = props.get(k.lower())
            dim = prop.data.get("array_dim", 1) if prop is not None else 1
            if dim > 1 and isinstance(v, list):
                v = {i: x for i, x in enumerate(v) if x is not None}
                if not v:
                    continue
            out[k] = to_json(v, resolve, notes, f"{p}.{k}")
        return out

    def stored_json(self, path: str, resolve=default_resolver, notes: Notes | None = None) -> dict:
        p = self.find(path)
        if p is None:
            return {}
        stored = self.classes[p].get("props") or {}
        return {k: to_json(v, resolve, notes, f"{p}.{k}") for k, v in stored.items()
                if not (isinstance(v, dict) and "_unknown_type" in v)}

    # ---- declarations --------------------------------------------------------------------------

    def describe_property(self, prop) -> dict:
        d = prop.data
        u = self.universe
        out: dict = {"name": prop.name, "type": PROPERTY_TYPES.get(prop.kind, prop.kind)}
        if d.get("array_dim", 1) != 1:
            out["arrayDim"] = d["array_dim"]
        out["flags"] = f"0x{d.get('flags', 0):08x}"
        if d.get("category") and d["category"] != "None":
            out["category"] = d["category"]
        for key, label in (("enum", "enum"), ("cls", "class"), ("meta", "metaClass"), ("struct", "struct")):
            if d.get(key):
                out[label] = u.path(prop.pkg, d[key])
        if prop.kind in ("ArrayProperty", "FixedArrayProperty") and d.get("inner"):
            inner = u.field_ref(prop.pkg, d["inner"])
            if inner is not None:
                out["inner"] = self.describe_property(inner)
            if prop.kind == "FixedArrayProperty":
                out["count"] = d.get("count", 0)
        return out

    def own_properties(self, path: str) -> List[dict]:
        f = self.field(path)
        if f is None:
            return []
        return [self.describe_property(c) for c in self.universe.children(f) if c.kind in self.cd.PROPERTY_EXTRA]

    def enums_and_structs(self) -> tuple[dict, dict]:
        enums: Dict[str, list] = {}
        structs: Dict[str, dict] = {}
        for (pkgname, _), f in sorted(self.universe.fields.items(), key=lambda kv: (kv[0][0], kv[0][1])):
            if pkgname not in {n.lower() for n in self.packages}:
                continue
            if f.kind == "Enum" and "names" in f.data:
                enums[f.path] = list(f.data["names"])
            elif f.kind == "Struct" and "error" not in f.data:
                sup = self.universe.path(f.pkg, f.super_ref) if f.super_ref else None
                structs[f.path] = {"super": sup, "properties": [self.describe_property(c) for c in
                                                                self.universe.children(f)
                                                                if c.kind in self.cd.PROPERTY_EXTRA]}
        return enums, structs

    @cached_property
    def enums(self) -> dict:
        return self.enums_and_structs()[0]

    def enum_values(self, suffix: str) -> Optional[List[str]]:
        """Names of the enum whose path ends with suffix (".EGameMode")."""
        for path, values in self.enums.items():
            if path.endswith(suffix):
                return values
        return None

    def enum_names(self, path: str, name: str) -> Optional[List[str]]:
        prop = self.prop(path, name)
        if prop is None or prop.kind != "ByteProperty" or not prop.data.get("enum"):
            return None
        en = self.universe.field_ref(prop.pkg, prop.data["enum"])
        return list(en.data.get("names", [])) if en is not None else None


@lru_cache(maxsize=4)
def class_db(game: str) -> ClassDB:
    """One ClassDB per game root and process (decoding takes about a second)."""
    return ClassDB(Path(game))


def is_name(v: Any) -> bool:
    return isinstance(v, str) and v != "" and v.lower() != "none"


__all__ = ["ClassDB", "class_db", "Name", "Ref", "is_name"]
