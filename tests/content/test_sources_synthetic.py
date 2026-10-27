"""Readers in svmbuild.sources on hand-built bytes: UModel nodes and zones, MeshAnimation notifies, typed props."""

from __future__ import annotations

import math
import struct
from dataclasses import dataclass
from pathlib import Path

import pytest
from svmbuild.sources import animations, modeltail
from svmbuild.sources.env import import_formats
from svmbuild.sources.typedprops import Name, Notes, Ref, bag_to_json, default_resolver, to_json

NAMES = ["None", "Bone0", "Seq0", "PlayFootStep", "Grp"]


@dataclass
class FakeExport:
    name: str
    offset: int
    size: int
    flags: int = 0


class FakePackage:
    def __init__(self, data: bytes):
        self.data = data
        self.names = list(NAMES)
        self.names_obfuscated = False
        self.path = Path("Synth.u")

    def export_reader(self, e):
        from scctpkg.package import Reader  # noqa: PLC0415
        return Reader(self.data[e.offset:e.offset + e.size], 0, self, base=e.offset)


def _writer():
    import_formats()
    from scctformats._writer import Writer  # noqa: PLC0415
    return Writer()


def test_model_nodes_zones_and_leaf_hulls():
    w = _writer()
    w.name(0)                                    # empty tagged property list
    w.raw(bytes(25 + 16))                        # bounding box and sphere
    w.compact(1).vec3((0.0, 0.0, 1.0))           # vectors
    pts = [(0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (10.0, 10.0, 0.0), (0.0, 10.0, 0.0)]
    w.compact(4)
    for p in pts:
        w.vec3(p)
    w.compact(1)                                 # one node
    w.raw(struct.pack("<4f", 0.0, 0.0, 1.0, 0.0)).raw(struct.pack("<Q", 0x5))
    w.u8(0x01)
    for v in (0, 0, -1, -1, -1, 7, -1):          # pool, surf, back, front, coplanar, collision bound, render bound
        w.compact(v)
    w.raw(bytes(32)).u8(0).u8(2).u8(4).raw(struct.pack("<5i", 3, 4, 0, 0, -1))
    w.compact(1).compact(0).u32(0x20)            # surf: material None, PF_Semisolid
    for v in (0, 0, 0, 0):
        w.compact(v)
    w.raw(bytes(16)).f32(32.0).f32(0.0)
    w.compact(4)
    for i in range(4):
        w.compact(i).compact(0)
    w.i32(2).i32(2)                              # shared sides, zones
    w.compact(0).raw(struct.pack("<Q", 1)).u32(0)
    w.compact(5).raw(struct.pack("<Q", 6)).u32(0)
    w.compact(0)                                 # Polys
    w.compact(0)                                 # bounds
    w.compact(2).i32(11).i32(-1)                 # leaf hulls
    data = w.bytes()
    m = modeltail.read_model(FakePackage(data), FakeExport("Model0", 0, len(data)))
    assert len(m.points) == 4 and len(m.nodes) == 1 and len(m.surfs) == 1
    n = m.nodes[0]
    assert (n.back, n.front, n.coplanar, n.collision_bound) == (-1, -1, -1, 7)
    assert (n.zone_back, n.zone_front, n.num_verts, n.leaf, n.flags) == (0, 2, 4, (3, 4), 1)
    assert list(m.polygon(n)) == [0, 1, 2, 3]
    assert m.surfs[0].poly_flags == modeltail.PF_SEMISOLID
    assert [(z.actor, z.connectivity) for z in m.zones] == [(0, 1), (5, 6)]
    assert list(m.leaf_hulls) == [11, -1]


def _anim_bytes(notify_name_index: int) -> bytes:
    w = _writer()
    w.name(0).i32(0)
    w.compact(1).name(1).u32(0).i32(0)                       # one bone
    w.compact(1).raw(bytes(12)).f32(10.0).i32(0).u32(0)      # one chunk
    w.compact(1).i32(0)                                      # bone indices
    w.compact(1).u32(0)                                      # one track
    w.compact(1).raw(struct.pack("<4h", 0, 0, 0, 32767))
    w.compact(1).raw(struct.pack("<3h", 32, 64, -32))
    w.compact(1).raw(struct.pack("<H", 0))
    w.u32(0).compact(0).compact(0).compact(0)                # empty root track
    w.compact(1).f32(0.25).name(2).compact(1).name(4)        # sequence Seq0, group Grp
    w.i32(0).i32(10)
    w.compact(2)
    w.f32(0.5).name(0).compact(3)                            # notify object export 3
    w.f32(0.9).name(notify_name_index, 300).compact(0)       # script function notify (large global name index)
    w.f32(15.0)
    return w.bytes()


def test_animation_notifies_root_track_and_lead():
    data = _anim_bytes(3)
    a = animations.read_animation(FakePackage(data), FakeExport("Anim0", 0, len(data)))
    assert a.bones == ["Bone0"]
    s = a.sequences[0]
    assert (s.name, s.lead, s.groups, s.start_frame, s.num_frames, s.rate) == ("Seq0", 0.25, ["Grp"], 0, 10, 15.0)
    assert [(n.time, n.function, n.notify_ref) for n in s.notifies] == [(0.5, "None", 3), (pytest.approx(0.9), "PlayFootStep", 0)]
    root = animations.track_json(a.chunks[0].tracks[0])
    assert root["pos"] == [[1.0, 2.0, -1.0]] and root["time"] == [0]
    assert animations.notify_seconds(0.5, 10, 15.0) == pytest.approx(1.0 / 3.0)


def test_animation_trailing_bytes_are_an_error():
    data = _anim_bytes(3) + b"\x00"
    with pytest.raises(ValueError, match="left"):
        animations.read_animation(FakePackage(data), FakeExport("Anim0", 0, len(data)))


def test_typed_values_to_json():
    notes = Notes()

    def resolve(r: Ref):
        if r.ref > 0:
            return {"$actor": r.path.rsplit(".", 1)[-1]}
        return default_resolver(r)

    bag = {
        "Tag": Name("Door"), "Count": 3, "Speed": 1.5, "bOn": True, "Text": "hello",
        "Target": Ref(4, "Map.Mover4", False, "Map"), "Mesh": Ref(-2, "Pkg.Grp.Mesh", False, "Map"),
        "Kind": Ref(-3, "SBase.SFlag", True, "Map"), "None": Ref(0, "", False, "Map"),
        "Fixed": {0: 1.0, 2: 3.0}, "Struct": {"X": 1.0, "Y": 2.0, "Z": 3.0},
        "List": [Ref(5, "Map.Light5", False, "Map")], "Bad": math.nan,
        "Unknown": {"_unknown_type": "IntProperty", "_raw": "00"},
    }
    out = bag_to_json(bag, resolve, notes, "A")
    assert out["Tag"] == "Door" and out["Count"] == 3 and out["Speed"] == 1.5 and out["bOn"] is True
    assert out["Target"] == {"$actor": "Mover4"} and out["Mesh"] == {"$asset": "Pkg.Grp.Mesh"}
    assert out["Kind"] == {"$class": "SBase.SFlag"} and out["None"] is None
    assert out["Fixed"] == {"$elements": {"0": 1.0, "2": 3.0}}
    assert out["Struct"] == {"X": 1.0, "Y": 2.0, "Z": 3.0} and out["List"] == [{"$actor": "Light5"}]
    assert out["Bad"] is None and "Unknown" not in out
    assert notes.counts == {"non-finite-float": 1, "unknown-property": 1}
    assert to_json(Ref(-1, "Lonely", False), default_resolver, notes, "x") is None
    assert notes.counts["unresolved-ref"] == 1
