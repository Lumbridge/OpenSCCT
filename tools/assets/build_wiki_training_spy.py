"""Trim the converted spy to the clips used by browser training; reuse the wiki's existing textures.

Usage: python tools/assets/build_wiki_training_spy.py [.content/shared/chars/SPerso/ATT_01.glb]
The input is local converted content, never an original package or a runtime download.
"""
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
CLIPS = {"WaitStAlFd0", "fallstaldn0", "landStAlHi0", "JoggStAlFd0"}


def trim(source):
    raw = source.read_bytes()
    size, kind = struct.unpack_from("<II", raw, 12)
    assert kind == 0x4E4F534A
    doc = json.loads(raw[20:20 + size])
    binary = raw[28 + size:]
    animations = [a for a in doc["animations"] if a["name"] in CLIPS]
    missing = CLIPS - {a["name"] for a in animations}
    if missing:
        raise ValueError(f"Missing clips: {missing}")

    used = set()
    for mesh in doc["meshes"]:
        for primitive in mesh["primitives"]:
            used.update(primitive["attributes"].values())
            if "indices" in primitive:
                used.add(primitive["indices"])
    used.update(s["inverseBindMatrices"] for s in doc["skins"])
    for animation in animations:
        for sampler in animation["samplers"]:
            used.update((sampler["input"], sampler["output"]))

    indices = {old: new for new, old in enumerate(sorted(used))}
    views, accessors, buffer, view_map = [], [], bytearray(), {}
    for old in sorted(used):
        accessor = dict(doc["accessors"][old])
        view_id = accessor["bufferView"]
        if view_id not in view_map:
            view = dict(doc["bufferViews"][view_id])
            offset = view.get("byteOffset", 0)
            buffer.extend(b"\0" * (-len(buffer) % 4))
            view["byteOffset"] = len(buffer)
            buffer.extend(binary[offset:offset + view["byteLength"]])
            view_map[view_id] = len(views)
            views.append(view)
        accessor["bufferView"] = view_map[view_id]
        accessors.append(accessor)

    for mesh in doc["meshes"]:
        for primitive in mesh["primitives"]:
            primitive["attributes"] = {key: indices[value] for key, value in primitive["attributes"].items()}
            if "indices" in primitive:
                primitive["indices"] = indices[primitive["indices"]]
    for skin in doc["skins"]:
        skin["inverseBindMatrices"] = indices[skin["inverseBindMatrices"]]
    for animation in animations:
        for sampler in animation["samplers"]:
            for key in ("input", "output"):
                sampler[key] = indices[sampler[key]]
    for image in doc["images"]:
        image["uri"] = "grab-" + Path(image["uri"]).name
    for material in doc["materials"]:
        material.pop("extras", None)
    doc.pop("extras", None)
    doc["asset"].pop("extras", None)
    buffer.extend(b"\0" * (-len(buffer) % 4))
    doc.update(animations=animations, accessors=accessors, bufferViews=views, buffers=[{"byteLength": len(buffer)}])
    data = json.dumps(doc, separators=(",", ":")).encode()
    data += b" " * (-len(data) % 4)
    return (struct.pack("<III", 0x46546C67, 2, 28 + len(data) + len(buffer))
            + struct.pack("<II", len(data), 0x4E4F534A) + data
            + struct.pack("<II", len(buffer), 0x004E4942) + buffer)


if __name__ == "__main__":
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else ROOT / ".content/shared/chars/SPerso/ATT_01.glb"
    destination = ROOT / "src/OpenScct.Master/Web/assets/training-spy.glb"
    destination.write_bytes(trim(source))
    print(f"{destination.name}: {destination.stat().st_size:,} bytes, {len(CLIPS)} clips")
