"""ui/strategicmap/floor<n>.png of a converted map: the strategic map's floor pictures (spec/hud/strategic-map.md).

LevelInfo.MapFloors names one texture per floor (FloorMapTexture), lowest floor first; the textures live in the map's
texture packages (Aquarium_TXT.Minimap.aqa_minimap_0) or in the map itself (MoonD.Minimap.Floor0). Each is written as
a PNG under the floor's index, so the client finds floor n's picture without a table; the floor heights and the
snapshot camera stay in sim/gameplay.json (levelInfo). A floor whose texture cannot be read gets no picture (the page
says "No map picture") and a note.
"""

from __future__ import annotations

import io
from typing import Dict

from ..sources.typedprops import Ref
from .source import MapSource

PICTURE_DIR = "ui/strategicmap"


def build_strategic_map(src: MapSource) -> Dict[str, bytes]:
    """{"ui/strategicmap/floor<n>.png": PNG bytes} for every readable floor picture."""
    from scctpkg.texture import read_texture  # noqa: PLC0415

    floors = src.value(src.level_info, "MapFloors") or []
    out: Dict[str, bytes] = {}
    for i, floor in enumerate(floors if isinstance(floors, list) else []):
        ref = floor.get("FloorMapTexture") if isinstance(floor, dict) else None
        if not isinstance(ref, Ref) or ref.ref == 0:
            src.notes.add("strategic-map-picture", f"floor {i}: no texture")
            continue
        pkg, e = src.ref_export(ref)
        if pkg is None or e is None or e.class_name != "Texture":
            src.notes.add("strategic-map-picture", f"floor {i}: {ref.path} not found")
            continue
        try:
            tex = read_texture(pkg, e)
        except Exception as ex:  # noqa: BLE001 - one unreadable picture must not fail the map
            src.notes.add("strategic-map-picture", f"floor {i}: {ref.path}: {ex}")
            continue
        if tex is None or tex.image is None:
            src.notes.add("strategic-map-picture", f"floor {i}: {ref.path} not decoded")
            continue
        buf = io.BytesIO()
        tex.image.save(buf, format="PNG", optimize=False)
        out[f"{PICTURE_DIR}/floor{i}.png"] = buf.getvalue()
    return out
