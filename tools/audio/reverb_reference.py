"""Build source-bound EAX3 listener fixtures and inspect IRs without opening an audio input.

SCCT's native setter (retail 0x10b0c420) copies the fields below into a 112-byte
EAX3 listener block; native RoomRolloffFactor is clamped to at least 0.1.
This utility emits data and PCM analysis only. It never plays or records sound.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import hashlib
import json
import math
from pathlib import Path
import struct

EAX3_LISTENER_GUID = "a8fa6882-b476-11d3-bdb9-00c0f02ddf87"
FIELDS = (
    ("Environment", "I"), ("EnvironmentSize", "f"), ("EnvironmentDiffusion", "f"),
    ("Room", "i"), ("RoomHF", "i"), ("RoomLF", "i"),
    ("DecayTime", "f"), ("DecayHFRatio", "f"), ("DecayLFRatio", "f"),
    ("Reflections", "i"), ("ReflectionsDelay", "f"),
    ("ReflectionsPan.X", "f"), ("ReflectionsPan.Y", "f"), ("ReflectionsPan.Z", "f"),
    ("Reverb", "i"), ("ReverbDelay", "f"),
    ("ReverbPan.X", "f"), ("ReverbPan.Y", "f"), ("ReverbPan.Z", "f"),
    ("EchoTime", "f"), ("EchoDepth", "f"), ("ModulationTime", "f"), ("ModulationDepth", "f"),
    ("AirAbsorptionHF", "f"), ("HFReference", "f"), ("LFReference", "f"),
    ("RoomRolloffFactor", "f"), ("Flags", "I"),
)
FLAGS = {"bDecayTimeScale": 0x01, "bReflectionsScale": 0x02, "bReflectionsDelayScale": 0x04,
         "bReverbScale": 0x08, "bReverbDelayScale": 0x10, "bDecayHFLimit": 0x20,
         "bEchoTimeScale": 0x40, "bModulationTimeScale": 0x80}


def listener_values(params: dict) -> dict:
    """All UObject fields start at zero; params contains effective defaults plus stored overrides."""
    result = {}
    for name, kind in FIELDS:
        if name == "Environment":
            value = 0  # Native's non-null branch explicitly writes this, rather than choosing a named preset.
        elif name == "Flags":
            value = sum(bit for key, bit in FLAGS.items() if params.get(key, False))
        elif "." in name:
            group, axis = name.split(".")
            value = params.get(group, {}).get(axis, 0.0)
        else:
            value = params.get(name, 0)
        if name == "RoomRolloffFactor":
            value = max(0.1, value)
        if not isinstance(value, (int, float)) or not math.isfinite(value):
            raise ValueError(f"{name}: non-finite or non-numeric EAX value {value!r}")
        result[name] = int(value) if kind in "iI" else float(value)
    return result


def listener_block(params: dict) -> bytes:
    values = listener_values(params)
    return struct.pack("<" + "".join(kind for _, kind in FIELDS), *(values[name] for name, _ in FIELDS))


def inventory(content: Path, output: Path) -> dict:
    """Inventory current converted effects and export representative actual-map fixtures."""
    output.mkdir(parents=True, exist_ok=True)
    environments = {}
    class_counts = Counter()
    ranges = defaultdict(list)
    vector_uses = []
    null_zones = []
    zones = 0
    for file in sorted((content / "svmap").glob("*/audio/audio.json")):
        doc = json.loads(file.read_bytes())
        for zone in doc["zones"]:
            zones += 1
            if not zone.get("effect"):
                null_zones.append({"map": file.parents[1].name, "id": zone["id"]})
                continue
            effect = zone["effect"]
            params = effect.get("params", {})
            block = listener_block(params)
            digest = hashlib.sha256(block).hexdigest()
            info = environments.setdefault(digest, {"id": digest[:12], "sourceClass": effect["class"],
                "params": params, "nativeValues": listener_values(params), "locations": []})
            info["locations"].append({"map": file.parents[1].name, "id": zone["id"]})
            class_counts[effect["class"]] += 1
            for name, value in listener_values(params).items():
                ranges[name].append(value)
            for name in ("ReflectionsPan", "ReverbPan"):
                if any(params.get(name, {}).values()):
                    vector_uses.append({"map": file.parents[1].name, "id": zone["id"], name: params[name]})
    fixtures = []
    all_blocks = output / "environments"
    all_blocks.mkdir(exist_ok=True)
    for digest, environment in environments.items():
        file = all_blocks / (environment["id"] + ".eax3.bin")
        file.write_bytes(listener_block(environment["params"]))
        environment["blockFile"] = str(file.resolve())
        environment["blockSha256"] = digest
    wanted = ("Room", "StoneCorridor", "Sewerpipe", "Hangar", "Generic")
    for suffix in wanted:
        match = next((v for v in environments.values() if v["sourceClass"].lower().endswith("effect_" + suffix.lower())), None)
        if match:
            fixtures.append({"name": suffix, **match})
    # Include explicit isolated changes around one source-bound reference block.
    if fixtures:
        base = next((f for f in fixtures if f["name"] == "Room"), fixtures[0])
        variations = {"ReflectionsGain": {"Reflections": -10000}, "ReflectionsDelay": {"ReflectionsDelay": 0.08},
            "ReverbGain": {"Reverb": -10000}, "ReverbDelay": {"ReverbDelay": 0.08},
            "RoomHF": {"RoomHF": -6000}, "RoomLF": {"RoomLF": -6000},
            "DecayHFRatio": {"DecayHFRatio": 0.25}, "DecayLFRatio": {"DecayLFRatio": 0.25},
            "Diffusion": {"EnvironmentDiffusion": 0.25}}
        for name, change in variations.items():
            params = {**base["params"], **change}
            fixtures.append({"name": "Room_" + name, "syntheticVariationOf": base["locations"][0],
                             "params": params, "nativeValues": listener_values(params)})
        dry = {**base["params"], "Room": -10000, "RoomHF": 0, "RoomLF": 0,
               "Reflections": -10000, "Reverb": -10000}
        fixtures.append({"name": "Dry", "syntheticVariationOf": base["locations"][0],
                         "params": dry, "nativeValues": listener_values(dry)})
        fixtures.append({"name": "RoomRepeat", "repeatOf": "Room", "params": base["params"],
                         "nativeValues": listener_values(base["params"])})
    # Fully initialized null pointer branch, verified in retail setter assembly (0x10b0c420).
    null = {"EnvironmentSize": 42.5, "EnvironmentDiffusion": .21, "Room": -1000, "RoomHF": -2000,
            "DecayTime": 1.49, "DecayHFRatio": .5, "DecayLFRatio": 1.0, "Reflections": -2466,
            "ReflectionsDelay": .179, "Reverb": -1927, "ReverbDelay": .1, "EchoTime": .25,
            "ModulationTime": .25, "AirAbsorptionHF": -5, "HFReference": 5000, "LFReference": 250,
            "RoomRolloffFactor": .1}
    null.update({key: bool(bit & 0x3f) for key, bit in FLAGS.items()})
    fixtures.append({"name": "NullNative", "nativeBranch": "retail 0x10b0c420 ECX=0",
                     "params": null, "nativeValues": listener_values(null)})
    for case in fixtures:
        block = listener_block(case["params"])
        file = output / (case["name"] + ".eax3.bin")
        file.write_bytes(block)
        case["blockFile"] = str(file.resolve())
        case["blockSha256"] = hashlib.sha256(block).hexdigest()
    result = {"format": "openscct.eax3-reference/1", "nativeSetter": "retail 0x10b0c420",
        "listenerGuid": EAX3_LISTENER_GUID, "propertyId": 1, "blockBytes": 112,
        "schema": [{"name": name, "offset": i*4, "type": kind} for i,(name,kind) in enumerate(FIELDS)],
        "flagBits": FLAGS, "zoneCount": zones, "effectZones": sum(class_counts.values()),
        "uniqueNativeBlocks": len(environments), "classes": class_counts,
        "ranges": {name: {"min": min(values), "max": max(values)} for name, values in ranges.items()},
        "nonzeroPanZones": vector_uses, "nullEffectZones": null_zones,
        "environments": list(environments.values()), "fixtures": fixtures,
        "limits": ["No running original game was observed by this inventory.",
                   "NullNative is sourced from the native null-effect setter branch, not an authored map effect.",
                   "Actual-map settings are distinct from explicitly labelled single-parameter variations."]}
    (output / "parameters.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--content", type=Path, default=Path(".content"))
    parser.add_argument("--out", type=Path, default=Path("local/reverb-parity/reference-parameters"))
    args = parser.parse_args()
    result = inventory(args.content, args.out)
    print(json.dumps({k: result[k] for k in ("zoneCount", "effectZones", "uniqueNativeBlocks", "classes", "ranges", "nonzeroPanZones")}, indent=2))
