"""Magma package (.mgm) reader: the game's menu and HUD page files to svm.magma/1 JSON.

A Magma package is Latin-1 text in an XML dialect: a PACKAGE root with MATERIALS (texture file + texel region), FONTS
(bitmap font names) and CHILDREN holding Page, Area and Button nodes, which nest further areas in their own CHILDREN.
Every area has a FRAMERATE and widgets: IMAGE (material, blend mode), TEXT (font, alignment, string), AREAINST (an
instance of another area, with its own clock or synchronized with the page) and LIST (rows built from a Button
area). Each widget carries KEYFRAMES: frame index, interpolation into it, rect or position, rotation, corner colours
(COLOR1..4, signed 32-bit 0xAABBGGRR), the state colour and text attributes, and actions (GotoFrame, Stop) that run
when the area's clock reaches the frame.

The JSON keeps the data as found, with references resolved to indexes: a material link becomes the material's index,
an area link the area's index in `areas`; a link into another package stays {"package", "id"}. Colours stay raw (COLOR as unsigned 0xAABBGGRR, STATECOLOR and TEXTCOLOR as
[r, g, b, a] bytes). docs/HUD.md describes the format and how the page player reads it.
"""

from __future__ import annotations

import xml.etree.ElementTree as ET
from typing import Any

FORMAT = "svm.magma/1"

AREA_TAGS = {"Area": "area", "Page": "page", "Button": "button"}
WIDGET_TAGS = {"IMAGE": "image", "TEXT": "text", "AREAINST": "inst", "LIST": "list"}
INTERPOLATIONS = {"None": "none", "Linear": "linear", "Circle": "circle", "Square": "square", "Root": "root"}
MASKS = {"NOMASK": "none", "SETMASK": "set", "USEMASK": "use"}
TEXT_FLAGS = ("BOLD", "ITALICS", "UNDERLINED", "WRAPPING", "CLIPPING", "ELLIPSIS", "AUTOSIZED")
BUTTON_TIMINGS = ("ENABLED", "PRESSED", "DISABLED", "SELECTED", "OVER")


class MagmaError(ValueError):
    pass


def _text(node: ET.Element | None, tag: str, default: str = "") -> str:
    if node is None:
        return default
    child = node.find(tag)
    if child is None or child.text is None:
        return default
    return child.text


def _number(token: str) -> int | float:
    """Integral values stay ints so the JSON reads like the source (rects, frames); others are floats."""
    value = float(token)
    if value.is_integer() and "." not in token and "e" not in token.lower():
        return int(value)
    return value


def _numbers(text: str) -> list[int | float]:
    return [_number(t) for t in text.split()]


def _rgba(text: str) -> list[int]:
    parts = [int(round(float(t))) for t in text.split()]
    if len(parts) != 4:
        raise MagmaError(f"colour {text!r} is not 'r g b a'")
    return parts


def _abgr(text: str) -> int:
    """COLORn is a signed 32-bit int holding 0xAABBGGRR; stored unsigned."""
    return int(text) & 0xFFFFFFFF


def _children(node: ET.Element | None) -> list[ET.Element]:
    return [] if node is None else list(node)


def _collect_areas(node: ET.Element, out: list[ET.Element]) -> None:
    for child in node:
        if child.tag in AREA_TAGS:
            out.append(child)
        if child.tag == "CHILDREN" or child.tag in AREA_TAGS:
            _collect_areas(child, out)


class _Resolver:
    def __init__(self, package: str, area_ids: dict[str, int], material_ids: dict[str, int]):
        self.package = package
        self.area_ids = area_ids
        self.material_ids = material_ids
        self.unresolved: list[str] = []

    def area(self, link: ET.Element | None) -> int | dict | None:
        """An AREALINK / LINK / ITEMLINK: the area index, a foreign {package, id}, or None when empty."""
        if link is None:
            return None
        area_id = _text(link, "AREA")
        if not area_id:
            return None
        pkg = _text(link, "PACKAGE", self.package)
        if pkg.lower() != self.package.lower():
            return {"package": pkg, "id": int(area_id)}
        index = self.area_ids.get(area_id)
        if index is None:
            self.unresolved.append(f"area {area_id}")
        return index

    def material(self, link: ET.Element | None) -> int | dict | None:
        """A MATERIALLINK: the material index, a foreign {package, id}, or None when empty."""
        if link is None:
            return None
        mat_id = _text(link, "MATERIAL")
        if not mat_id:
            return None
        pkg = _text(link, "PACKAGE", self.package)
        if pkg.lower() != self.package.lower():
            return {"package": pkg, "id": int(mat_id)}
        index = self.material_ids.get(mat_id)
        if index is None:
            self.unresolved.append(f"material {mat_id}")
        return index


def _keyframe(k: ET.Element, res: _Resolver) -> dict[str, Any]:
    key: dict[str, Any] = {"f": int(_text(k, "IDX", "0"))}
    interp = _text(k, "INTERPOLATION", "None")
    key["interp"] = INTERPOLATIONS.get(interp, interp.lower())
    if any(k.find(t) is not None for t in ("LEFT", "TOP", "RIGHT", "BOTTOM")):
        key["rect"] = [_number(_text(k, t, "0")) for t in ("LEFT", "TOP", "RIGHT", "BOTTOM")]
    if (pos := _text(k, "POSITION")):
        key["pos"] = _numbers(pos)
    if (rot := _text(k, "ROTATION")):
        key["rot"] = _number(rot)
    if (center := _text(k, "ROTATIONCENTER")):
        key["rotCenter"] = _numbers(center)
    if (tiling := _text(k, "TILING")):
        key["tiling"] = _numbers(tiling)
    if (offset := _text(k, "OFFSET")):
        key["offset"] = _numbers(offset)
    if (c1 := _text(k, "COLOR1")):
        first = _abgr(c1)
        key["colors"] = [first] + [_abgr(_text(k, f"COLOR{i}")) if _text(k, f"COLOR{i}") else first for i in (2, 3, 4)]
    if (state := _text(k, "STATECOLOR")):
        key["state"] = _rgba(state)
    if (color := _text(k, "TEXTCOLOR")):
        key["color"] = _rgba(color)
    if (shadow := _text(k, "SHADOWCOLOR")):
        key["shadow"] = _rgba(shadow)
    if k.find("SHADOWOFFSETX") is not None or k.find("SHADOWOFFSETY") is not None:
        key["shadowOffset"] = [_number(_text(k, "SHADOWOFFSETX", "0")), _number(_text(k, "SHADOWOFFSETY", "0"))]
    for tag, name in (("HEIGHT", "height"), ("OFFSETY", "offsetY"), ("LEADING", "leading"), ("TRACKING", "tracking")):
        if (value := _text(k, tag)):
            key[name] = _number(value)
    actions = []
    for act in _children(k.find("ACTIONS")):
        if act.tag == "GotoFrame":
            actions.append({"op": "goto", "area": res.area(act.find("AREALINK")),
                            "frame": int(_text(act, "FRAMEIDX", "0"))})
        elif act.tag == "Stop":
            actions.append({"op": "stop", "area": res.area(act.find("AREALINK"))})
        else:
            actions.append({"op": act.tag})
    if actions:
        key["actions"] = actions
    return key


def _widget(w: ET.Element, res: _Resolver) -> dict[str, Any]:
    kind = WIDGET_TAGS[w.tag]
    out: dict[str, Any] = {"kind": kind, "name": _text(w, "NAME"), "id": int(_text(w, "ID", "0"))}
    if _text(w, "HIDDEN") == "1":
        out["hidden"] = True
    mask = _text(w, "MASKMODE", "NOMASK")
    out["mask"] = MASKS.get(mask, mask.lower())
    keys = [_keyframe(k, res) for k in w.iterfind("KEYFRAMES/KEYFRAME")]
    keys.sort(key=lambda key: key["f"])
    out["keys"] = keys
    if kind == "image":
        out["material"] = res.material(w.find("MATERIALLINK"))
        out["blend"] = _text(w, "BLENDINGMODE", "Normal")
    elif kind == "text":
        out["font"] = _text(w.find("FONTLINK"), "NAME")
        out["align"] = _text(w, "ALIGNMENT", "LEFT").lower()
        out["text"] = _text(w, "STRING")
        flags = [f.lower() for f in TEXT_FLAGS if _text(w, f) == "1"]
        if flags:
            out["flags"] = flags
    elif kind == "inst":
        link = w.find("LINK")
        out["area"] = res.area(link)
        timing = _text(link, "TIMING", "None")
        out["timing"] = timing
        out["sync"] = timing.startswith("Synchronized")
    elif kind == "list":
        out["rows"] = int(_text(w, "BUTTONCOUNT", "0"))
        out["item"] = res.area(w.find("ITEMLINK"))
        out["header"] = res.area(w.find("HEADERLINK"))
        out["footer"] = res.area(w.find("FOOTERLINK"))
        out["headerFooter"] = _text(w, "HEADERFOOTERPOS")
    return out


def parse_mgm(data: bytes | str) -> dict[str, Any]:
    """Converts one Magma package. Raises MagmaError on structural problems."""
    text = data.decode("latin-1") if isinstance(data, (bytes, bytearray)) else data
    try:
        root = ET.fromstring(text)
    except ET.ParseError as e:
        raise MagmaError(f"not a Magma package: {e}") from e
    if root.tag != "PACKAGE":
        raise MagmaError(f"root element is {root.tag}, expected PACKAGE")
    name = _text(root, "NAME")
    size = _numbers(_text(root, "PAGESIZE", "640 480"))

    materials = []
    material_ids: dict[str, int] = {}
    for m in root.iterfind("MATERIALS/MATERIAL"):
        stage = m.find("STAGE")
        file = _text(stage, "FILE")
        base = file.replace("\\", "/").rsplit("/", 1)[-1]
        texture = base.rsplit(".", 1)[0] if "." in base else base
        region = _numbers(_text(stage, "REGION", "0 0 0 0"))
        material_ids[_text(m, "ID")] = len(materials)
        materials.append({"id": int(_text(m, "ID", "0")), "name": _text(m, "NAME"), "file": file,
                          "texture": texture, "region": region})

    fonts = [_text(p, "NAME") for p in root.iterfind("FONTS/Pixmap")]

    nodes: list[ET.Element] = []
    _collect_areas(root, nodes)
    area_ids = {}
    for i, n in enumerate(nodes):
        area_id = _text(n, "ID")
        if area_id in area_ids:
            raise MagmaError(f"area id {area_id} appears twice")
        area_ids[area_id] = i
    res = _Resolver(name, area_ids, material_ids)

    areas = []
    pages = {}
    skipped: dict[str, int] = {}
    for i, n in enumerate(nodes):
        kind = AREA_TAGS[n.tag]
        area: dict[str, Any] = {"id": int(_text(n, "ID", "0")), "name": _text(n, "NAME"), "kind": kind,
                                "frameRate": _number(_text(n, "FRAMERATE", "10"))}
        if kind == "button":
            timings = n.find("TIMINGS")
            area["timings"] = {t.lower(): int(_text(timings, t, "0")) for t in BUTTON_TIMINGS}
        widgets = []
        for w in _children(n.find("CHILDREN")):
            if w.tag in WIDGET_TAGS:
                widgets.append(_widget(w, res))
            elif w.tag not in AREA_TAGS:
                skipped[w.tag] = skipped.get(w.tag, 0) + 1
        area["widgets"] = widgets
        if kind == "page":
            pages[area["name"]] = i
        areas.append(area)

    doc: dict[str, Any] = {"format": FORMAT, "name": name, "version": _text(root, "VERSION"),
                           "pageSize": size, "materials": materials, "fonts": fonts, "areas": areas, "pages": pages}
    if res.unresolved:
        doc["unresolved"] = sorted(set(res.unresolved))
    if skipped:
        doc["skippedWidgets"] = skipped
    return doc


def textures_used(doc: dict[str, Any]) -> list[str]:
    """Texture names the package's materials point at, in first-use order (once each, ignoring case)."""
    seen: list[str] = []
    lower: set[str] = set()
    for m in doc["materials"]:
        if m["texture"] and m["texture"].lower() not in lower:
            seen.append(m["texture"])
            lower.add(m["texture"].lower())
    return seen
