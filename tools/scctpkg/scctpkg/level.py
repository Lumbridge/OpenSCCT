"""Extracts a map's placed actors into plain records for export.

Handles StaticMeshActor (and SCCT subclasses that carry a StaticMesh),
Light classes, PlayerStart classes, Movers, ZoneInfo/SkyZoneInfo and keeps a
tagged marker for everything else so the runtime bridge can match by name.
"""

from __future__ import annotations

import json
import struct
import os
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

from .package import Export, Package
from .packageset import PackageSet
from .properties import read_properties, skip_state_frame

LIGHT_CLASSES = {"Light", "Spotlight", "TriggerLight", "STriggerLight", "SunLight", "SLight", "SSpotLight",
                 "SunlightCorona", "STriggerLightBeam"}
PLAYERSTART_CLASSES = {"PlayerStart", "STriggerPlayerStart", "SPlayerStart"}
ZONE_CLASSES = {"ZoneInfo", "SkyZoneInfo", "WarpZoneInfo", "SZoneInfo"}
PROJECTOR_CLASSES = {"Projector", "DynamicProjector"}
SKIP_CLASSES = {"Model", "Polys", "StaticMesh", "StaticMeshInstance", "Texture", "Shader", "Package",
                "LevelSummary", "Level", "Palette", "Font", "Camera", "Brush", "Sound", "SMapSettings"}
# Class defaults (DrawType, bHidden, bHideInNormalVision, StaticMesh) of 274 actor classes, read from the running
# game's class default objects. A map only stores a property when it differs from the class default, so these decide
# whether a placed actor is drawn (DrawType 8 = DT_StaticMesh) and hidden: SGamePlayObject, Mover, SLift default to
# DT_Sprite, path guides and triggers default to bHidden.
with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), "class_defaults.json"), encoding="utf-8") as _f:
    CLASS_DEFAULTS: Dict[str, dict] = json.load(_f)
# "trigger_" is not a helper word: trigger_hack, trigger_urgence, trigger_scan and Trigger_Vert are the hack panels,
# emergency buttons and scanners the game draws. A sprite SInterrupteur (trigger_standard) is hidden by its DrawType.
# Not "fakewarp": fakewarp_aqa is the big Aqua* tank's backdrop (green sand floor and algae wall), drawn by the game.
HIDDEN_MESH_WORDS = ("help_path", "path_circle", "nullobj", "_collision", "blocking")
DT_STATICMESH = 8

# UE2 ELightType / ELightEffect values, read from the live game's Engine.Actor enums.
LT_NONE = 0
LE_STATICSPOT = 8
LE_SPOTLIGHT = 12
LE_ESPOTSHADOW = 22        # SCCT projector spot

# Light corona defaults (Light class default object in the running game).
CORONA_DEFAULTS = {"MinCoronaSize": 0.0, "MaxCoronaSize": 1000.0, "CoronaRotation": 0.0, "CoronaRotationOffset": 0.0}
# UE2 Projector defaults (Engine.Projector class default object in the running game); maps store only what differs.
# FrameBufferBlendingOp defaults to PB_Modulate (1): maps store 0 (ShadD, CpusS) but never 1.
PROJECTOR_DEFAULTS = {"FOV": 0, "MaxTraceDistance": 1000, "FrameBufferBlendingOp": 1, "MaterialBlendingOp": 0,
                      "bProjectBSP": True, "bProjectStaticMesh": True, "bProjectOnBackfaces": False, "bClipBSP": False,
                      "bGradient": False, "bProjectOnParallelBSP": False}


def compact_refs(raw: bytes, count: int) -> List[int]:
    """Decode `count` FCompactIndex object references from raw bytes (an object array the parser could not type)."""
    out, i = [], 0
    for _ in range(count):
        if i >= len(raw):
            break
        b0 = raw[i]
        i += 1
        v = b0 & 0x3F
        if b0 & 0x40:
            shift = 6
            while i < len(raw):
                b = raw[i]
                i += 1
                v |= (b & 0x7F) << shift
                shift += 7
                if not b & 0x80:
                    break
        out.append(-v if b0 & 0x80 else v)
    return out


def object_refs(v) -> List[int]:
    """Object array property value (list, single int, or the parser's raw {'_count','_raw'} dict) -> refs."""
    if isinstance(v, list):
        return [int(x) if isinstance(x, int) else 0 for x in v]
    if isinstance(v, int):
        return [v]
    if isinstance(v, dict) and "_raw" in v:
        return compact_refs(v["_raw"], int(v.get("_count", 0)))
    return []


def corona_color(hue: int, sat: int) -> Tuple[float, float, float]:
    """UE2 corona colour (render code at 0x10a138a9): hue ramp in 1/85 steps, then lerp to white by saturation/255."""
    h = hue & 0xFF
    if h < 86:
        c = ((85 - h) / 85.0, h / 85.0, 0.0)
    elif h < 171:
        c = (0.0, (170 - h) / 85.0, (h - 85) / 85.0)
    else:
        c = ((h - 170) / 85.0, 0.0, (255 - h) / 84.0)
    t = max(0, min(255, sat)) / 255.0
    return tuple(round(x + (1.0 - x) * t, 4) for x in c)


@dataclass
class ActorRecord:
    name: str
    cls: str
    kind: str                         # staticmesh | light | playerstart | mover | zone | marker
    location: Tuple[float, float, float] = (0.0, 0.0, 0.0)
    rotation: Tuple[int, int, int] = (0, 0, 0)
    scale: Tuple[float, float, float] = (1.0, 1.0, 1.0)
    mesh_key: str = ""                # "Package.Mesh"
    mesh_ref: int = 0
    tag: str = ""
    group: str = ""
    extra: Dict = field(default_factory=dict)
    hidden: bool = False


def _num(v, default: float) -> float:
    if isinstance(v, (int, float)):
        return float(v)
    if isinstance(v, list):
        for x in v:
            if isinstance(x, (int, float)):
                return float(x)
    return default


def _scale_of(props) -> Tuple[float, float, float]:
    s = _num(props.get("DrawScale"), 1.0) or 1.0
    s3 = props.get("DrawScale3D")
    if isinstance(s3, (tuple, list)) and len(s3) == 3 and all(isinstance(x, (int, float)) for x in s3):
        out = (s * s3[0], s * s3[1], s * s3[2])
    else:
        out = (s, s, s)
    # Misparsed or editor-junk scales (seen: 559648 on a ZoneInfo) would break rendering bounds.
    if any(abs(x) > 1000.0 or x == 0.0 for x in out):
        return (1.0, 1.0, 1.0)
    return out


def _hsv_to_rgb(hue: int, sat: int, brightness: float) -> Tuple[float, float, float]:
    """UE2 light colour as the game computes it (FGetHSV, decoded from the exe): a hue triangle R->G->B over 0..255
    (85 per ramp), pulled toward white by Saturation / 255 (255 = white). FGetHSV's brightness factor (0.82 at V 255)
    is left out here; the client's UE2 pawn lighting applies it."""
    h = float(hue)
    if h < 86:
        r, g, b = (85.0 - h) / 85.0, h / 85.0, 0.0
    elif h < 171:
        r, g, b = 0.0, (170.0 - h) / 85.0, (h - 85.0) / 85.0
    else:
        r, g, b = (h - 170.0) / 85.0, 0.0, (255.0 - h) / 84.0
    s = sat / 255.0
    return tuple(float(max(0.0, min(1.0, c + (1.0 - c) * s)) * brightness) for c in (r, g, b))


def _class_default(cls: str) -> dict:
    d = CLASS_DEFAULTS.get(cls)
    if d is not None:
        return d
    # Classes missing from the dump (map-local subclasses such as SFish, MusicTrigger): triggers are hidden helpers.
    return {"dt": None, "hidden": 1 if cls.endswith("Trigger") else 0, "hideNV": 0, "sm": ""}


def _corona(props, pkg: Package, pset: PackageSet, trig: bool) -> Optional[dict]:
    """Corona parameters of a light (UE2 draws Skins[0] as a camera-facing halo while bCorona is set)."""
    skins = object_refs(props.get("Skins"))
    tex_ref = skins[0] if skins else 0
    on = bool(props.get("bCorona"))
    if not on and not (trig and tex_ref):
        return None     # trigger lights can switch a corona on at runtime (bInitialCorona); keep those
    g = lambda k: _num(props.get(k), CORONA_DEFAULTS[k])
    s3 = props.get("DrawScale3D")
    s3 = list(s3) if isinstance(s3, (tuple, list)) and len(s3) == 3 else [1.0, 1.0, 1.0]
    hue = int(_num(props.get("LightHue"), 0))
    sat = int(_num(props.get("LightSaturation"), 255))
    return {
        "texture": pset.import_path(pkg, tex_ref) if tex_ref else "",
        "texture_ref": tex_ref,
        "min": g("MinCoronaSize"), "max": g("MaxCoronaSize"),
        "rotation": g("CoronaRotation"), "rotation_offset": g("CoronaRotationOffset"),
        "draw_scale": _num(props.get("DrawScale"), 1.0), "scale3d": s3,
        "scale_glow": _num(props.get("ScaleGlow"), 1.0),
        "hue": hue, "saturation": sat, "color": list(corona_color(hue, sat)),
        "radius": _num(props.get("LightRadius"), 64.0), "cone": int(_num(props.get("LightCone"), 128)),
        "orient": bool(props.get("bCoronaUsesOrientation")), "own_blend": bool(props.get("UseOwnFinalBlend")),
        "on": on,
    }


PLF_PSX2_ONLY, PLF_XBOX_ONLY = 1, 2


# Engine.ZoneInfo class defaults of the fog fields (def:Engine.ZoneInfo): a zone stores only what differs, so a fog zone
# without DistanceFogColor fogs to grey 128 (ClubD's ZoneInfo1141, MissD's three outdoor zones), one without
# DistanceFogEnd ends at 8000.
ZONE_FOG_DEFAULTS = {"DistanceFogColor": [128, 128, 128, 0], "DistanceFogStart": 3000.0, "DistanceFogEnd": 8000.0,
                     "DistanceFogBlendTime": 1.0}


def _hsv_color(hue: int, sat: int, bright: int) -> List[int]:
    """UE2 FGetHSV(H, S, V) as bytes (truncated): the zone light colour of AmbientHue/Saturation/Brightness. Every
    stored m_RefAmbientColor of the 84 maps (2814 zones) equals this, so it stands in where a map does not store one."""
    import math
    b = bright * 1.4 / 255.0
    b *= 0.7 / (0.01 + math.sqrt(b))
    b = min(max(b, 0.0), 1.0)
    if hue < 86:
        h = ((85 - hue) / 85.0, hue / 85.0, 0.0)
    elif hue < 171:
        h = (0.0, (170 - hue) / 85.0, (hue - 85) / 85.0)
    else:
        h = ((hue - 170) / 85.0, 0.0, (255 - hue) / 84.0)
    return [int((c + sat / 255.0 * (1.0 - c)) * b * 255.0) for c in h] + [255]


def zone_ambient_color(props: dict) -> List[int]:
    """A zone's ambient light colour [R, G, B, A]: m_RefAmbientColor, else FGetHSV of AmbientHue, AmbientSaturation
    (default 255) and AmbientBrightness (default 0)."""
    amb = props.get("m_RefAmbientColor")
    if isinstance(amb, (tuple, list)) and len(amb) >= 3:
        return list(amb)
    return _hsv_color(int(_num(props.get("AmbientHue"), 0)), int(_num(props.get("AmbientSaturation"), 255)),
                      int(_num(props.get("AmbientBrightness"), 0)))


def _zone_ambient(pkg: Package) -> dict:
    """ZoneInfo / SkyZoneInfo object ref -> ambient brightness (max of RGB, 0-255)."""
    out = {}
    for e in pkg.exports:
        if not e.class_name.endswith("ZoneInfo") or e.size <= 0 or not (e.flags & 0x02000000):
            continue
        try:
            r = pkg.export_reader(e)
            skip_state_frame(r, e.flags)
            amb = zone_ambient_color(read_properties(r))
        except Exception:
            continue
        out[e.index + 1] = int(max(amb[:3]))
    return out


# Soft-body class defaults (Engine.ESoftBodyActor and the SoftBody package's class default objects, classes step).
_SOFTBODY_COMMON = {"Gravity": (0.0, 0.0, -980.0), "friction": 0.0, "nbIter": 1, "nbNormalizeIter": 1, "collActor": True,
                    "EnergyFactor": 0.0, "hitDamping": 1.0, "dragForce": 60.0, "BulletHitFactor": 25, "Mass": 100.0,
                    "nbU": 3, "nbV": 3, "ULength": 10.0, "VLength": 10.0, "scaleU": 1.0, "scaleV": 1.0, "FixedTop": True}
SOFTBODY_DEFAULTS = {
    "ESBPatchActor": {**_SOFTBODY_COMMON, "nbIter": 3, "nbNormalizeIter": 2, "windMin": (0.0, -100.0, 0.0),
                      "windMax": (0.0, 300.0, 0.0), "windUScale": 2.0, "windVScale": 2.0, "windUPan": 0.7, "windVPan": 0.31},
    "ESBStripDoorActor": {**_SOFTBODY_COMMON, "nbIter": 3, "nbNormalizeIter": 2, "windMin": (0.0, -100.0, 0.0),
                          "windMax": (0.0, 100.0, 0.0), "windUScale": 1.0, "windVScale": 1.0, "windUPan": 0.5,
                          "windVPan": 0.21},
}
_SB_PARTICLE = 72   # FSBParticle: flags, position, old position, acceleration, wind, hit force, u, v


def _sb_state(pkg: Package, ref: int):
    """The particles and links of a placed soft body's simulation object (ESBPatch / ESBStripDoor export), as the
    editor saved them: UESoftBody::Serialize (0x110d7800 in the editor) writes the primitive's bounds, two ints, the
    owner, an int, then the particle array (72 bytes each: flags bit 0 = fixed, position, old position, acceleration,
    wind, hit force, u, v; positions in the actor's local space) and the link array (two particle indices and the rest
    length). Returns (flags, points, uvs, links) or None when the object is missing or does not read."""
    if not isinstance(ref, int) or ref <= 0 or ref > len(pkg.exports):
        return None
    e = pkg.exports[ref - 1]
    if e.size <= 0:
        return None
    try:
        r = pkg.export_reader(e)
        skip_state_frame(r, e.flags)
        read_properties(r)
        r.raw(25 + 16 + 8)      # bounding box, bounding sphere, two ints
        r.compact()             # the owning actor
        r.i32()
        n = r.compact()
        if n <= 0 or n > 4096:
            return None
        flags, points, uvs = [], [], []
        for _ in range(n):
            raw = r.raw(_SB_PARTICLE)
            f = struct.unpack_from("<I", raw, 0)[0]
            v = struct.unpack_from("<17f", raw, 4)
            flags.append(f & 1)
            points.extend(v[0:3])
            uvs.extend(v[15:17])
        m = r.compact()
        if m < 0 or m > 16384:
            return None
        links = []
        for _ in range(m):
            a, b = r.i32(), r.i32()
            rest = r.f32()
            if not (0 <= a < n and 0 <= b < n):
                return None
            links.append((a, b, rest))
    except Exception:
        return None
    if not all(abs(x) < 1e6 and x == x for x in points):
        return None
    return flags, points, uvs, links


def _sb_built(cls: str, name: str, nu: int, nv: int, ul: float, vl: float, fixed: dict):
    """The rest layout the build gives a soft body, for when its saved object does not read (the layout read from 70
    saved bodies): a patch is a (nU + 1) x (nV + 1) grid in local XZ, centred, row by row from the top-left, linked to
    its right and lower neighbours; a strip door is nU strips of ULength side by side, each two edges of nV + 1
    particles (the left one top down, the right one bottom up), linked down each edge and across, top row fixed."""
    w, h = nu * ul, nv * vl
    flags, points, uvs, links = [], [], [], []
    if cls == "ESBStripDoorActor":
        # Each strip shows a random 1/nU wide slice of the texture (the saved bodies' u); estimated from the name.
        seed = sum(ord(c) * (i + 1) for i, c in enumerate(name))
        for k in range(nu):
            seed = (seed * 1103515245 + 12345) & 0x7FFFFFFF
            off = (seed / 0x7FFFFFFF) * (1.0 - 1.0 / nu)
            base = len(flags)
            for side in (0, 1):
                x = -w / 2 + (k + side) * ul
                rows = range(nv + 1) if side == 0 else range(nv, -1, -1)
                for j in rows:
                    flags.append(1 if j == 0 else 0)
                    points.extend((x, 0.0, h / 2 - j * vl))
                    uvs.extend((off + side / nu, j / nv))
            for j in range(nv):
                links.append((base + j, base + j + 1, vl))
                links.append((base + 2 * nv + 1 - j, base + 2 * nv - j, vl))
                links.append((base + j + 1, base + 2 * nv - j, ul))
        return flags, points, uvs, links
    for j in range(nv + 1):
        for i in range(nu + 1):
            top, bottom, left, right = j == 0, j == nv, i == 0, i == nu
            f = ((top and fixed["FixedTop"]) or (bottom and fixed["FixedBottom"]) or (left and fixed["FixedLeft"])
                 or (right and fixed["FixedRight"]) or (top and left and fixed["FixedTopLeft"])
                 or (top and right and fixed["FixedTopRight"]) or (bottom and left and fixed["FixedBottomLeft"])
                 or (bottom and right and fixed["FixedBottomRight"]))
            flags.append(1 if f else 0)
            points.extend((-w / 2 + i * ul, 0.0, h / 2 - j * vl))
            uvs.extend((i / nu, j / nv))
    for j in range(nv + 1):
        for i in range(nu + 1):
            a = j * (nu + 1) + i
            for b, rest in ((a + 1, ul) if i < nu else (None, 0.0), (a + nu + 1, vl) if j < nv else (None, 0.0)):
                if b is not None and not (flags[a] and flags[b]):
                    links.append((a, b, rest))
    return flags, points, uvs, links


def softbody_record(pkg: Package, cls: str, props: dict) -> Optional[dict]:
    """The simulation settings and rest state of a placed soft body (stored values over the class defaults), the way
    UESoftBody copies them from its actor each build (0x1099d090 in the game): friction and hitDamping clamped to 0..1,
    dragForce to 0..10000."""
    d = SOFTBODY_DEFAULTS[cls]
    g = lambda k: props.get(k, d.get(k))
    vec = lambda k: [float(x) for x in g(k)] if isinstance(g(k), (tuple, list)) and len(g(k)) == 3 else list(d[k])
    nu, nv = max(1, int(_num(g("nbU"), 3))), max(1, int(_num(g("nbV"), 3)))
    ul, vl = _num(g("ULength"), 10.0), _num(g("VLength"), 10.0)
    state = _sb_state(pkg, props.get("SoftBody"))
    expected = 2 * nu * (nv + 1) if cls == "ESBStripDoorActor" else (nu + 1) * (nv + 1)
    source = "map"
    if state is None or len(state[0]) != expected:
        fixed = {k: bool(props.get(k, d.get(k, False))) for k in
                 ("FixedTop", "FixedBottom", "FixedLeft", "FixedRight", "FixedTopLeft", "FixedTopRight",
                  "FixedBottomLeft", "FixedBottomRight")}
        state = _sb_built(cls, str(props.get("Tag", "")) + str(props.get("Location", "")), nu, nv, ul, vl, fixed)
        source = "built"
    flags, points, uvs, links = state
    clamp = lambda v, lo, hi: max(lo, min(hi, v))
    return {
        "kind": "strip" if cls == "ESBStripDoorActor" else "patch", "source": source,
        "nb_u": nu, "nb_v": nv, "u_length": ul, "v_length": vl,
        "scale_u": _num(g("scaleU"), 1.0), "scale_v": _num(g("scaleV"), 1.0),
        "gravity": vec("Gravity"), "wind_min": vec("windMin"), "wind_max": vec("windMax"),
        "wind_u_scale": _num(g("windUScale"), 0.0), "wind_v_scale": _num(g("windVScale"), 0.0),
        "wind_u_pan": _num(g("windUPan"), 0.0), "wind_v_pan": _num(g("windVPan"), 0.0),
        "friction": clamp(_num(g("friction"), 0.0), 0.0, 1.0),
        "nb_iter": int(_num(g("nbIter"), 1)), "nb_normalize_iter": int(_num(g("nbNormalizeIter"), 1)),
        "coll_actor": bool(g("collActor")), "energy_factor": _num(g("EnergyFactor"), 0.0),
        "hit_damping": clamp(_num(g("hitDamping"), 1.0), 0.0, 1.0),
        "drag_force": clamp(_num(g("dragForce"), 60.0), 0.0, 10000.0),
        "bullet_hit_factor": int(_num(g("BulletHitFactor"), 25)), "mass": _num(g("Mass"), 100.0),
        "fixed": [i for i, f in enumerate(flags) if f],
        "points": [round(float(x), 3) for x in points],
        "uvs": [round(float(x), 5) for x in uvs],
        "links": [[int(a), int(b), round(float(rest), 3)] for a, b, rest in links],
    }


def extract_level(pkg: Package, pset: PackageSet) -> List[ActorRecord]:
    records: List[ActorRecord] = []
    zone_ambient = _zone_ambient(pkg)
    for e in pkg.exports:
        cls = e.class_name
        if cls in SKIP_CLASSES or e.size <= 0:
            continue
        if not (e.flags & 0x02000000):
            continue  # not an actor (no state frame)
        r = pkg.export_reader(e)
        try:
            skip_state_frame(r, e.flags)
            props = read_properties(r)
        except Exception:
            props = {}
        if props.get("bDeleteMe") or props.get("bPendingDelete"):
            continue   # deleted editor ghosts stay in the package but never exist in game
        if int(_num(props.get("Platform"), 0)) == PLF_PSX2_ONLY and cls != "Emitter":
            # PS2-only actors (Engine.u EPlatform: 0 COMMON, 1 PSX2_Only, 2 XBOX_Only). The PC Versus build keeps the
            # XBOX_Only ones: live on MuseD, Light16 (XBOX_Only projector) lights the spawn wall and spy (switching it
            # off drops the wall 59 -> 14) and XBOX_Only vents/conduits are loaded, DrawType 8, not hidden. 85eee5c
            # dropped all 1620 of them. Emitters stay whatever their flag (20aa357): the client draws an emitter only
            # while its live actor exists, so a marker the game really drops costs nothing.
            continue
        loc = props.get("Location")
        loc = tuple(loc) if isinstance(loc, (tuple, list)) and len(loc) == 3 else (0.0, 0.0, 0.0)
        rot = props.get("Rotation")
        rot = tuple(rot) if isinstance(rot, (tuple, list)) and len(rot) == 3 else (0, 0, 0)
        cdef = _class_default(cls)
        # bHidden / DrawType are stored only when they differ from the class default.
        hidden = bool(props["bHidden"]) if "bHidden" in props else bool(cdef.get("hidden"))
        draw_type = int(_num(props.get("DrawType"), -1)) if "DrawType" in props else cdef.get("dt")
        rec = ActorRecord(name=e.name, cls=cls, kind="marker", location=loc, rotation=rot, scale=_scale_of(props),
                          tag=str(props.get("Tag", "")), group=str(props.get("Group", "")), hidden=hidden)
        mesh_ref = props.get("StaticMesh")
        if isinstance(mesh_ref, int) and mesh_ref != 0 and draw_type is not None and draw_type != DT_STATICMESH:
            # The game does not draw the static mesh of a sprite/none/mesh actor (e.g. DT_Sprite SInterrupteur with
            # trigger_standard): keep a hidden marker so the runtime still finds it by name.
            rec.hidden = True
            rec.extra["draw_type"] = draw_type
            rec.extra["static_mesh"] = pset.import_path(pkg, mesh_ref)
            mesh_ref = None
        if isinstance(mesh_ref, int) and mesh_ref != 0:
            rec.mesh_ref = mesh_ref
            rec.mesh_key = pset.import_path(pkg, mesh_ref)
            rec.kind = "mover" if (cls == "Mover" or "Mover" in cls or cls == "SLift") else "staticmesh"
            if any(w in rec.mesh_key.lower() for w in HIDDEN_MESH_WORDS):
                rec.hidden = True
            smi = props.get("StaticMeshInstance")
            if isinstance(smi, int) and smi > 0:
                rec.extra["smi"] = smi
            # Actor.bUnlit: drawn fullbright (texture only), its baked vertex colours unused - MissE's grass tufts
            # store near-black colours but show their green texture in the game.
            if props.get("bUnlit"):
                rec.extra["unlit"] = True
            # Actor.AmbientGlow (0-255; 5611 mesh actors on 83 maps) raises the actor's own lighting on top of its baked
            # vertex colours: BankD's sky-zone skyline has glow 180 and near-black colours, and the game draws it lit.
            glow = int(_num(props.get("AmbientGlow"), 0))
            if props.get("bSpecialLit"):
                # bSpecialLit meshes take the zone ambient instead of baked light: ClubD's sky backdrop (CLU_warp,
                # baked colours all 0) sits in SkyZoneInfo1724 with m_RefAmbientColor 149 and the game draws it at
                # tex x 2 x 149/255 [I from the classic frame, ClubD p3].
                region = props.get("Region") if isinstance(props.get("Region"), dict) else {}
                glow = max(glow, zone_ambient.get(region.get("Zone"), 0))
            if glow > 0:
                rec.extra["ambient_glow"] = glow
            # UE2 stops drawing the actor beyond CullDistance (510 mesh actors in the Versus maps, 1000-5000 uu).
            if props.get("CullDistance"):
                rec.extra["cull_distance"] = float(props["CullDistance"])
            pre = props.get("PrePivot")
            if isinstance(pre, (tuple, list)):
                rec.extra["prepivot"] = list(pre)
            # Actor.Skins[i] replaces the material of mesh section i (0 = keep the mesh's own material).
            refs = object_refs(props.get("Skins"))
            if any(refs):
                rec.extra["skin_refs"] = refs
                rec.extra["skins"] = [pset.import_path(pkg, s) if s else "" for s in refs]
            if rec.kind == "mover":
                kp = props.get("KeyPos")
                if isinstance(kp, list):
                    rec.extra["keypos"] = [list(k) if isinstance(k, (tuple, list)) else None for k in kp]
                bp = props.get("BasePos")
                if isinstance(bp, (tuple, list)):
                    rec.extra["basepos"] = list(bp)
        elif cls in LIGHT_CLASSES or (props.get("LightBrightness") is not None and props.get("LightRadius") is not None):
            rec.kind = "light"
            rec.hidden = bool(props.get("bHidden"))   # Light.bHidden only hides the editor sprite
            effect = int(_num(props.get("LightEffect"), 0))
            ltype = int(_num(props.get("LightType"), 1))
            rec.extra = {
                "brightness": _num(props.get("LightBrightness"), 64.0),
                "radius": _num(props.get("LightRadius"), 64.0),
                "hue": int(_num(props.get("LightHue"), 0)),
                "saturation": int(props.get("LightSaturation", 255) or 0),
                "color": list(_hsv_to_rgb(int(_num(props.get("LightHue"), 0)), int(props.get("LightSaturation", 255) if props.get("LightSaturation") is not None else 255), 1.0)),
                "light_type": "spot" if effect in (LE_SPOTLIGHT, LE_STATICSPOT, LE_ESPOTSHADOW) or cls in ("Spotlight", "SSpotLight") else "point",
                "cone": int(_num(props.get("LightCone"), 128)),
                "effect": effect,
                "ltype": ltype,
                "echelon_range": _num(props.get("EchelonRange"), 0.0),
                "spot_width": _num(props.get("SpotWidth"), 0.0),
                "spot_height": _num(props.get("SpotHeight"), 0.0),
                # LE_ESpotShadow frustum (decoded, docs/LIGHTING_BALANCE.md): apex NearPlaneRatio x EchelonRange /
                # (1 - NearPlaneRatio) behind the light. Actor default 0.1.
                "near_plane_ratio": _num(props.get("NearPlaneRatio"), 0.1),
                # LT_Pulse / Blink / Flicker / Strobe / SubtlePulse timing (UE2 Light defaults: period 32, phase 0).
                "light_period": _num(props.get("LightPeriod"), 32),
                "light_phase": _num(props.get("LightPhase"), 0),
            }
            # STriggerLight.bInitialyOn defaults to False and is only saved when True; switch/trigger groups
            # (e.g. FactD's "Disco" room) start off. Plain lights are always on unless LT_None.
            trig = cls in ("STriggerLight", "TriggerLight")
            on = bool(props.get("bInitialyOn", props.get("bInitiallyOn", False))) if trig else True
            rec.extra.update({
                "initially_on": bool(on and (trig or ltype != LT_NONE)),
                "triggered": trig,
                # SCCT light flags: static lights are already in the lightmaps/vertex colours; in-game lights
                # light dynamic actors (pawns); dynamic lights are applied to the world at runtime.
                "apply_static": bool(props.get("bApplyToStaticLighting", True)),
                "apply_ingame": bool(props.get("bApplyToInGameLighting", False)),
                "dynamic": bool(props.get("bDynamicLight", trig)),
                # bAffectOwnZoneOnly: the light reaches only actors in its own zone (MissE's outdoor Br 255 radius 180
                # fill lights stop at the building: the bench spy is a silhouette in the game, lit grey without this).
                "own_zone_only": bool(props.get("bAffectOwnZoneOnly", False)),
            })
            reg = props.get("Region")
            if isinstance(reg, dict) and isinstance(reg.get("Zone"), int) and reg["Zone"]:
                try:
                    rec.extra["zone"] = pkg.object_name(int(reg["Zone"]))
                except Exception:
                    pass
            em = props.get("EchelonLightMaterial")
            if effect == LE_ESPOTSHADOW and isinstance(em, int) and em:
                # EchelonLightingMaterial: the projector throws its ProjectedLightingTexture over the frustum box
                # (RiveD's neon "River Mall" logo, BankD's projectors); the exporter writes the texture.
                rec.extra["echelon_material_ref"] = em
            corona = _corona(props, pkg, pset, trig)
            if corona is not None:
                rec.extra["corona"] = corona
            if ltype == LT_NONE and cls == "Light":
                rec.hidden = True
        elif cls in PROJECTOR_CLASSES and isinstance(props.get("ProjTexture"), int) and props.get("ProjTexture"):
            # UE2 texture projector (painted wall signs): export_gltf clips the BSP under it into a decal mesh.
            rec.kind = "projector"
            g = lambda k: props.get(k, PROJECTOR_DEFAULTS[k])
            rec.extra = {
                "proj_texture": pset.import_path(pkg, int(props["ProjTexture"])),
                "proj_texture_ref": int(props["ProjTexture"]),
                "fov": _num(g("FOV"), 0.0), "max_trace": _num(g("MaxTraceDistance"), 1000.0),
                "fb_blend": int(_num(g("FrameBufferBlendingOp"), 0)), "mat_blend": int(_num(g("MaterialBlendingOp"), 0)),
                "project_bsp": bool(g("bProjectBSP")), "project_static_mesh": bool(g("bProjectStaticMesh")),
                "backfaces": bool(g("bProjectOnBackfaces")), "clip_bsp": bool(g("bClipBSP")),
                "gradient": bool(g("bGradient")), "parallel": bool(g("bProjectOnParallelBSP")),
                "draw_scale": _num(props.get("DrawScale"), 1.0),
            }
        elif cls in PLAYERSTART_CLASSES:
            rec.kind = "playerstart"
            rec.extra = {"team": "merc" if int(_num(props.get("TeamNumber"), 0)) == 1 else "spy"}
        elif cls in ZONE_CLASSES:
            rec.kind = "zone"
            fog_color = props.get("DistanceFogColor")
            rec.extra = {
                "location_name": str(props.get("LocationName", "")),
                "fog": bool(props.get("bDistanceFog")),
                "fog_color": list(fog_color) if isinstance(fog_color, (tuple, list)) else list(ZONE_FOG_DEFAULTS["DistanceFogColor"]),
                "fog_start": _num(props.get("DistanceFogStart"), ZONE_FOG_DEFAULTS["DistanceFogStart"]),
                "fog_end": _num(props.get("DistanceFogEnd"), ZONE_FOG_DEFAULTS["DistanceFogEnd"]),
                # Seconds the view's fog takes to move to this zone's (0: at once).
                "fog_blend_time": _num(props.get("DistanceFogBlendTime"), ZONE_FOG_DEFAULTS["DistanceFogBlendTime"]),
                # With bDistanceFog the main view clears to DistanceFogColor before drawing.
                "clear_to_fog": bool(props.get("bClearToFogColor")),
                "ambient": zone_ambient_color(props),
                "sky": cls == "SkyZoneInfo",
            }
        elif cls == "ERainVolume":
            # World-space rain the game draws itself inside a box, seen from outside too (OrphD/OrphE and 91 volumes
            # in all). Location sits on the floor: m_RainSize is the box, rising m_RainSize.Z from there [I from OrphD's
            # BSP floors]. The client builds the sheets from these (SCCTEffects.cpp UpdateRainVolumes).
            wp = props.get("m_weatherProperties") if isinstance(props.get("m_weatherProperties"), dict) else {}
            size = props.get("m_RainSize")
            # Fallbacks are the ERainVolume class defaults (maps store only what differs); m_RainFalloff has none (0 in
            # the game, where RenderRain would divide by it): 100 here, the value every shipped volume but Stat*'s sets.
            rec.extra["rain"] = {
                "size": list(size) if isinstance(size, (tuple, list)) and len(size) == 3 else [250.0, 250.0, 450.0],
                "falloff": _num(props.get("m_RainFalloff"), 100.0),
                "fade_scale": _num(props.get("m_RainFadeScale"), -1.0),
                "distance_to_camera": _num(props.get("m_DistanceToCamera"), 80.0),
                # RenderRain draws no sheet farther than this from the camera.
                "max_dist": _num(props.get("m_maxDistFromCamera"), 1000.0),
                "thickness": _num(wp.get("m_RainThickness"), 80.0),
                "mat_scale": _num(wp.get("m_RainMatScale"), 10.0),
            }
            mref = wp.get("m_RainMaterial")
            if isinstance(mref, int) and mref:
                rec.extra["rain"]["material"] = pset.import_path(pkg, mref)
                rec.extra["rain_material_ref"] = mref
        elif cls in SOFTBODY_DEFAULTS:
            # Soft body (ESBPatchActor cloth: AquaE's sea, awnings, flags; ESBStripDoorActor plastic strip curtains),
            # simulated at runtime from its ESBPatch / ESBStripDoor object (softbody_record). AquaE's sea is a patch
            # (10 x 10, 2000 x 1900 uu under the terrace); AmbientGlow is its only light (bIgnoreEchelonLighting).
            # Maps store nbU, nbV and the rest only when they differ from the class default (ClarD's flags keep
            # nbU 3), so every value falls back to it.
            tex = props.get("Texture")
            skins = object_refs(props.get("Skins"))
            mref = tex if isinstance(tex, int) and tex else (skins[0] if skins and skins[0] else 0)
            body = softbody_record(pkg, cls, props) if mref else None
            if body is not None:
                body["ambient_glow"] = int(_num(props.get("AmbientGlow"), 0))
                body["unlit"] = bool(props.get("bUnlit"))
                rec.extra["softbody"] = body
                rec.extra["patch_material_ref"] = mref
        elif rec.kind == "marker" and cdef.get("sm") and "StaticMesh" not in props:
            # Class-default mesh (SLaserTrigger housings, SCamNetwork globes, SInfirmary cabinets): drawn by the
            # runtime from /Game/SCCT/Shared; recorded for tools. Instances that store another DrawType (61 DT_Sprite
            # SInterrupteur, 8 SVolumetricSensor) do not draw it: hidden marker with their draw_type, as above.
            if draw_type == DT_STATICMESH:
                rec.extra["default_mesh"] = cdef["sm"]
            elif draw_type is not None:
                rec.hidden = True
                rec.extra["draw_type"] = draw_type
        records.append(rec)
    _split_group_clashes(pkg, pset, records)
    return records


def _split_group_clashes(pkg: Package, pset: PackageSet, records: List[ActorRecord]) -> None:
    """Mesh keys are Package.Name; where placed meshes of one name sit in different groups (CpusS's three
    PCF09_STM...Porte doors, TSP21's Light / Object tsp02_lamp), those records key by their full path instead, so
    each draws its own mesh rather than the first one loaded."""
    paths: Dict[str, set] = {}
    for rec in records:
        if rec.mesh_ref:
            paths.setdefault(rec.mesh_key, set()).add(pset.object_path(pkg, rec.mesh_ref))
    for rec in records:
        if rec.mesh_ref and len(paths.get(rec.mesh_key, ())) > 1:
            rec.mesh_key = pset.object_path(pkg, rec.mesh_ref)


# LevelInfo.GlowThreshold class default (live LevelInfo0 / Entry): the native glow post pass brightpasses at T/255.
GLOW_THRESHOLD_DEFAULT = 224


def extract_look(pkg: Package) -> dict:
    """Per-map post-process settings stored on the LevelInfo (only when they differ from the class default)."""
    look = {"glow_threshold": GLOW_THRESHOLD_DEFAULT, "glow_threshold_stored": False}
    for e in pkg.exports_of_class("LevelInfo"):
        if e.size <= 0:
            continue
        r = pkg.export_reader(e)
        try:
            skip_state_frame(r, e.flags)
            props = read_properties(r)
        except Exception:
            continue
        if "GlowThreshold" in props:
            look["glow_threshold"] = int(_num(props["GlowThreshold"], GLOW_THRESHOLD_DEFAULT))
            look["glow_threshold_stored"] = True
        if "Brightness" in props:
            look["brightness"] = _num(props["Brightness"], 1.0)
        break
    # 255 disables the pass (SteeD/E/P/T); T = glow_threshold / 255.
    look["glow_enabled"] = look["glow_threshold"] < 255
    return look
