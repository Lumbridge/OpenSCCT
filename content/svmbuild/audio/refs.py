"""Where sounds are referenced: map actors (every actor of the level, also the client-only AmbientSound actors that
sim/gameplay.json leaves out), zone ambiences and zone reverb (ZoneInfo.ZoneEffect, an I3DL2Listener object such as
EFFECT_Room in the map package), and class defaults (weapons, gadgets, pawns, movers...).

A reference counts when it resolves to a Sound of the shared library (Packages/Sounds/*.uax), matched by path
ignoring case. Values that only scripts pass to PlaySound live in the bytecode and are not listed here; the client
resolves any path against the library at play time.
"""

from __future__ import annotations

from typing import Any, Iterable

from ..sources.typedprops import Ref

# Actor categories of mapconv.source that exist in the PC game.
LIVE_CATEGORIES = ("level", "gameplay", "render")

# Properties every actor reads for its ambient loop (Actor's Sound group).
AMBIENT_FIELDS = ("SoundVolume", "SoundRadius", "SoundPitch", "bFullVolume", "SoundOcclusion", "LightType",
                  "LightBrightness")

# I3DL2Listener fields, in declaration order.
I3DL2_FIELDS = ("EnvironmentSize", "EnvironmentDiffusion", "Room", "RoomHF", "RoomLF", "DecayTime", "DecayHFRatio",
                "DecayLFRatio", "Reflections", "ReflectionsDelay", "ReflectionsPan", "Reverb", "ReverbDelay",
                "ReverbPan", "EchoTime", "EchoDepth", "ModulationTime", "ModulationDepth", "RoomRolloffFactor",
                "AirAbsorptionHF", "HFReference", "LFReference", "bDecayTimeScale", "bReflectionsScale",
                "bReflectionsDelayScale", "bReverbScale", "bReverbDelayScale", "bEchoTimeScale",
                "bModulationTimeScale", "bDecayHFLimit")


def walk_refs(value: Any, path: str = "") -> Iterable[tuple[str, Ref]]:
    if isinstance(value, Ref):
        yield path, value
    elif isinstance(value, dict):
        for k in sorted(value, key=str):
            yield from walk_refs(value[k], f"{path}.{k}" if path else str(k))
    elif isinstance(value, (list, tuple)):
        for i, v in enumerate(value):
            yield from walk_refs(v, f"{path}[{i}]")


class SoundIndex:
    """Lookup of library sound paths ignoring case."""

    def __init__(self, paths: Iterable[str]):
        self._by_lower = {p.lower(): p for p in paths}

    def find(self, path: str | None) -> str | None:
        return self._by_lower.get(path.lower()) if path else None

    def __contains__(self, path: str) -> bool:
        return self.find(path) is not None


def _num(v: Any) -> Any:
    if isinstance(v, bool) or v is None:
        return v
    if isinstance(v, (int, float)):
        return v
    return None


def _vec(v: Any) -> dict | None:
    if isinstance(v, dict) and {"X", "Y", "Z"} <= set(v):
        return {"X": float(v["X"]), "Y": float(v["Y"]), "Z": float(v["Z"])}
    return None


def class_sound_refs(db, sounds: SoundIndex) -> dict[str, dict[str, str]]:
    """Class path -> property path -> sound path, from the effective defaults of every script class."""
    out: dict[str, dict[str, str]] = {}
    for cls in sorted(db.classes, key=str.lower):
        found = {}
        for prop, ref in walk_refs(db.localized.get(cls, {})):
            hit = sounds.find(ref.path)
            if hit is not None:
                found[prop] = hit
        if found:
            out[cls] = found
    return out


def map_sound_refs(src, sounds: SoundIndex) -> dict:
    """Sound references and zone data of one opened map (a mapconv.source.MapSource)."""
    actors = []
    zones = []
    missing: dict[str, list[str]] = {}
    db = src.db
    for info in src.actors:
        if info.category not in LIVE_CATEGORIES:
            continue
        stored = {}
        for prop, ref in walk_refs(info.props):
            if ref.ref == 0:
                continue
            hit = sounds.find(ref.path)
            if hit is None:
                pkg, exp = src.ref_export(ref)
                if exp is not None and exp.class_name == "Sound":
                    hit = sounds.find(exp.full_name)
                    if hit is None:
                        missing.setdefault(exp.full_name, []).append(f"{info.name}.{prop}")
            if hit is not None:
                stored[prop] = hit
        ambient_ref = src.value(info, "AmbientSound")
        ambient = sounds.find(ambient_ref.path) if isinstance(ambient_ref, Ref) and ambient_ref.ref != 0 else None
        is_zone = db.is_a(info.cls, "Engine.ZoneInfo")
        effect = zone_effect(src, info) if is_zone else None
        if not stored and ambient is None and effect is None:
            continue
        rec = {
            "id": info.name,
            "class": info.cls,
            "category": info.category,
            "location": _vec(src.value(info, "Location")) or {"X": 0.0, "Y": 0.0, "Z": 0.0},
            "sounds": stored,
        }
        if ambient is not None:
            rec["ambientSound"] = ambient
            for f in AMBIENT_FIELDS:
                v = _num(src.value(info, f))
                if v is not None:
                    rec[f[0].lower() + f[1:]] = v
        if info.category != "render":
            region = src.value(info, "Region")
            if isinstance(region, dict) and isinstance(region.get("ZoneNumber"), int):
                rec["zoneNumber"] = region["ZoneNumber"]
        actors.append(rec)
        if is_zone:
            z = {"id": info.name, "class": info.cls}
            if ambient is not None:
                z["ambientSound"] = ambient
                z["soundVolume"] = _num(src.value(info, "SoundVolume"))
                z["soundPitch"] = _num(src.value(info, "SoundPitch"))
                z["soundRadius"] = _num(src.value(info, "SoundRadius"))
            if effect is not None:
                z["effect"] = effect
            zones.append(z)
    return {"actors": actors, "zones": zones, "missing": {k: sorted(v) for k, v in sorted(missing.items())}}


def zone_effect(src, info) -> dict | None:
    """The I3DL2 reverb of a ZoneInfo: ZoneEffect's class and its effective parameters (class default overlaid by
    what the map stores on the object)."""
    ref = src.value(info, "ZoneEffect")
    if not isinstance(ref, Ref) or ref.ref == 0:
        return None
    db = src.db
    pkg, exp = src.ref_export(ref)
    if exp is None:
        return {"object": ref.path, "error": "not found"}
    cls = db.find(f"{exp.class_package}.{exp.class_name}") or f"{exp.class_package}.{exp.class_name}"
    out = {"object": f"{pkg.path.stem}.{exp.name}", "class": cls}
    stored: dict = {}
    rec = db.record(cls)
    if rec is None:
        out["error"] = "class not in the script packages"
    elif exp.size > 0:
        try:
            stored = db.decoder.tagged(pkg.export_reader(exp), pkg, rec["_field"])
        except Exception as e:  # noqa: BLE001 - reported, the zone keeps its class defaults
            out["error"] = f"{type(e).__name__}: {e}"
    params = {}
    for f in I3DL2_FIELDS:
        v = stored.get(f, db.default(cls, f))
        if isinstance(v, dict):
            v = _vec(v)
        elif not isinstance(v, (bool, int, float)):
            v = None
        if v is not None:
            params[f] = v
    out["params"] = params
    out["stored"] = sorted(k for k in stored if k in I3DL2_FIELDS)
    return out


def zone_distances(src) -> list[list[int]]:
    """The portal distance between every two zones of the map's world model (ULevel ZoneDist, game 0x109ea770):
    0 to itself, the step of the expansion below that first reaches it, else 255. Rows and columns follow the zone
    numbers (the collision ZONE records). The occlusion and the ambient range of the sound system scale with it."""
    from ..sources import modeltail  # noqa: PLC0415

    ref = src.header.model_a or src.header.model_b
    if not ref:
        return []
    model = modeltail.read_model(src.pkg, src.pkg.exports[ref - 1])
    n = len(model.zones)
    if n == 0:
        return []
    mask = (1 << 64) - 1
    # Each zone's reach starts as its Connectivity mask (zones past the model's count only reach themselves); every
    # step replaces a zone's reach by the union of the reaches of the zones it reaches, so the reach doubles.
    reach = [model.zones[i].connectivity & mask if i < n else 1 << i for i in range(64)]
    dist = [[0 if i == j else 255 for j in range(64)] for i in range(64)]
    for step in range(1, 64):
        for i in range(64):
            row = dist[i]
            for j in range(64):
                if row[j] > step and reach[i] >> j & 1:
                    row[j] = step
        nxt = []
        for i in range(64):
            acc = 0
            for k in range(64):
                if reach[i] >> k & 1:
                    acc |= reach[k]
            nxt.append(acc)
        if nxt == reach:  # nothing more is reached: the later steps change nothing
            break
        reach = nxt
    return [dist[i][:n] for i in range(n)]
