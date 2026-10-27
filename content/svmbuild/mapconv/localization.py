"""The build language's text over a map's stored text (svmbuild --language).

The original game takes a localized property's text from System/_PC_/<package>.<language> when it loads an object,
whatever the package stores, and retail packages store French text (the Steam copy's DTK09 zone tips, missions and
briefings). So the map converter does the same: <Map>.int (or .fra, ...) has a section per actor (its export name),
<Map>-i.int a [MapSettings] section for the SMapSettings object, ModesStrings among it as a struct array:

    ModesStrings=((ExplicationModeSpy§(6553)="...",ExplicationModeSentry§(6550)="..."),(...))

(the §(n) after a member name is a string id the reader skips). Only properties flagged localized are taken, and only
text: a value of another kind stays as the package stores it.
"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Any, Callable, Dict, Optional

_MEMBER = re.compile(r'\s*([A-Za-z_]\w*)(?:[^=,()"]|\(\d+\))*=')


def read_text(game: Path, package: str) -> Dict[str, Dict[str, Any]]:
    """The language's sections of <package> (lower-case section -> key -> index -> value), or {}."""
    from ..sources.env import import_formats  # noqa: PLC0415

    import_formats()
    import scctformats.classdefaults as cd  # noqa: PLC0415

    from ..gamefiles import text_dir  # noqa: PLC0415

    return cd.read_localization(text_dir(Path(game), package), package)


def parse_struct_value(text: str) -> Any:
    """A struct or array value of a text file: (A="x",B="y") -> dict, ("a","b") / ((...),(...)) -> list."""
    pos = 0
    n = len(text)

    def skip() -> None:
        nonlocal pos
        while pos < n and text[pos].isspace():
            pos += 1

    def value() -> Any:
        nonlocal pos
        skip()
        if pos >= n:
            return ""
        if text[pos] == "(":
            pos += 1
            items: list = []
            members: dict = {}
            while True:
                skip()
                if pos >= n:
                    break
                if text[pos] == ")":
                    pos += 1
                    break
                m = _MEMBER.match(text, pos)
                if m:
                    pos = m.end()
                    members[m.group(1)] = value()
                else:
                    items.append(value())
                skip()
                if pos < n and text[pos] == ",":
                    pos += 1
            return members if members else items
        if text[pos] == '"':
            pos += 1
            out = []
            while pos < n and text[pos] != '"':
                if text[pos] == "\\" and pos + 1 < n:
                    pos += 1
                out.append(text[pos])
                pos += 1
            pos += 1
            return "".join(out)
        start = pos
        while pos < n and text[pos] not in ",)":
            pos += 1
        return text[start:pos].strip()

    return value()


def _merge_text(stored: Any, text: Any) -> Any:
    """The strings of a parsed text value over a stored struct / array value of the same shape."""
    if isinstance(text, str):
        return text if stored is None or isinstance(stored, str) else stored
    if isinstance(text, dict) and isinstance(stored, dict):
        out = dict(stored)
        by_lower = {k.lower(): k for k in stored}
        for k, v in text.items():
            key = by_lower.get(k.lower(), k)
            merged = _merge_text(stored.get(key), v)
            if merged is not None:
                out[key] = merged
        return out
    if isinstance(text, list) and isinstance(stored, list):
        out = list(stored)
        for i, v in enumerate(text[:len(stored)]):
            out[i] = _merge_text(stored[i], v)
        return out
    return stored


def apply_text(props: Dict[str, Any], section: Optional[Dict[str, Dict[Any, Any]]], prop_of: Callable[[str], Any]) -> int:
    """Puts a section's text over an object's decoded props; returns how many properties changed."""
    if not section:
        return 0
    from ..sources.env import import_formats  # noqa: PLC0415

    import_formats()
    import scctformats.classdefaults as cd  # noqa: PLC0415

    changed = 0
    for key, by_index in section.items():
        prop = prop_of(key)
        if prop is None or not prop.data.get("flags", 0) & cd.CPF_LOCALIZED:
            continue
        name = prop.name
        dim = prop.data.get("array_dim", 1)
        if prop.kind == "StrProperty" and dim > 1:
            cur = dict(props[name]) if isinstance(props.get(name), dict) else {}
            for i, v in by_index.items():
                values = list(enumerate(v[:dim])) if isinstance(v, list) else [(i, v)]
                for j, x in values:
                    # An empty text for an element the package does not store is the class default anyway.
                    if isinstance(x, str) and j is not None and j < dim and (x or j in cur):
                        cur[j] = x
            if cur and cur != props.get(name):
                props[name] = cur
                changed += 1
            continue
        v = by_index.get(None, next(iter(by_index.values())))
        if prop.kind == "StrProperty":
            if isinstance(v, str) and (v or name in props) and props.get(name) != v:
                props[name] = v
                changed += 1
        elif isinstance(v, dict) and "$struct" in v and name in props:
            merged = _merge_text(props[name], parse_struct_value(v["$struct"]))
            if merged != props[name]:
                props[name] = merged
                changed += 1
    return changed
