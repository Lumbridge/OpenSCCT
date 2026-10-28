"""sim/rain.json of a converted map: the map's ERainVolumes as the simulation needs them.

The rain volumes are drawn from render/fx.json, but they also matter to the game rules: a pawn standing in one is wet
(ASBasePawn::Tick checks the pawn's location against every rain volume's box each tick), which cools it in thermal,
lengthens the Shocker freeze, halves a laser tripwire's heat and blocks the spy's cloak. A server can run without the
render part, so the boxes are written to the sim part too.

Layout (svm.rain/1):

    {"format": "svm.rain/1",
     "volumes": [{"name": "ERainVolume418", "location": [x, y, z], "scale": [sx, sy, sz],
                  "size": [x, y, z], "falloff": 100.0}]}

location is the floor of the box, scale is DrawScale x DrawScale3D, size is m_RainSize, falloff m_RainFalloff (the
box is (-X, -Y, 0)..(X, Y, Z) of size x scale from location, docs/RENDERING.md#rain-volumes).
"""

from __future__ import annotations

from ..sources.typedprops import bag_to_json, default_resolver

FORMAT = "svm.rain/1"
RAIN_CLASS = "Engine.ERainVolume"

# Engine.ERainVolume and Engine.Actor class defaults for the values a map may leave out.
DEFAULT_SIZE = (250.0, 250.0, 450.0)
DEFAULT_FALLOFF = 0.0


def _vec(value, fallback):
    if isinstance(value, dict) and all(k in value for k in ("X", "Y", "Z")):
        return [float(value["X"]), float(value["Y"]), float(value["Z"])]
    if isinstance(value, (list, tuple)) and len(value) == 3:
        return [float(v) for v in value]
    return [float(v) for v in fallback]


def build_rain(src) -> dict:
    """The rain document of a map source (every ERainVolume the map places, in level order)."""
    volumes = []
    for info in src.actors:
        if info.cls != RAIN_CLASS or info.category == "skipped":
            continue
        props = bag_to_json(info.props, default_resolver, src.notes, info.name)
        draw = float(props.get("DrawScale", 1.0))
        draw3d = _vec(props.get("DrawScale3D"), (1.0, 1.0, 1.0))
        volumes.append({
            "name": info.name,
            "location": _vec(props.get("Location"), (0.0, 0.0, 0.0)),
            "scale": [draw * s for s in draw3d],
            "size": _vec(props.get("m_RainSize"), DEFAULT_SIZE),
            "falloff": float(props.get("m_RainFalloff", DEFAULT_FALLOFF)),
        })
    return {"format": FORMAT, "volumes": volumes}
