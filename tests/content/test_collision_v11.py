"""collision.bin 1.1 chunks (CSRC, TFLX, CYLX): round trip, 1.0 compatibility and the count rules."""

from __future__ import annotations

import struct

import pytest
from svmbuild.writers import collision as C


def _data(extended: bool) -> C.CollisionData:
    d = C.CollisionData()
    if extended:
        d.sources, d.material_ext, d.cylinder_ext = [], [], []
    a = d.string("Box0")
    d.verts += [(0.0, 0.0, 0.0), (10.0, 0.0, 0.0), (0.0, 10.0, 0.0)]
    d.materials.append(C.Material(5, C.flag_bits(["semisolid"], C.MATERIAL_FLAGS), d.string("Pkg.Grp.Mat")))
    d.tris.append((0, 1, 2, 0, 0))
    d.colliders.append(C.Collider(kind=2, flags=3, actor=a, vert_start=0, vert_count=3, tri_start=0, tri_count=1,
                                  aabb_max=(10.0, 10.0, 0.0)))
    d.cylinders.append(C.Cylinder(a, 1, (0.0, 0.0, 0.0), 40.0, 40.0))
    d.zones.append(C.Zone(a, 0, (0.0, 0.0, 0.0), (1.0, 1.0, 1.0)))
    if extended:
        d.sources.append(C.ColliderSource(a, d.string("Pkg.Grp.Mesh"), 2, 0b101, 1))
        d.material_ext.append(C.MaterialExt(0x20, 0b011, 2))
        d.cylinder_ext.append(C.CylinderExt(1, d.string("SBase.STriggerInteraction")))
    return d


def test_without_extensions_the_file_is_1_0():
    data = C.write_collision(_data(False))
    assert struct.unpack_from("<HH", data, 4) == (1, 0)
    assert struct.unpack_from("<I", data, 8)[0] == 10
    d = C.read_collision(data)
    assert d.sources is None and d.material_ext is None and d.cylinder_ext is None


def test_extensions_round_trip():
    data = C.write_collision(_data(True))
    assert struct.unpack_from("<HH", data, 4) == (1, 1)
    assert struct.unpack_from("<I", data, 8)[0] == 13
    d = C.read_collision(data)
    assert d.sources == [C.ColliderSource(0, 2, 2, 0b101, 1)]
    assert d.material_ext == [C.MaterialExt(0x20, 0b011, 2)]
    assert d.cylinder_ext[0].kind == 1 and d.strings[d.cylinder_ext[0].interaction_class] == "SBase.STriggerInteraction"
    assert C.write_collision(d) == data


def test_a_1_0_reader_skips_the_extensions():
    """A reader that knows only the 1.0 chunks sees them as unknown chunks it can skip by size."""
    data = C.write_collision(_data(True))
    pos, seen = 16, []
    for _ in range(struct.unpack_from("<I", data, 8)[0]):
        cc, _ver, size, _count = struct.unpack_from("<4sIII", data, pos)
        seen.append(cc)
        pos = (pos + 16 + size + 15) & ~15
    assert tuple(seen[:10]) == C.CHUNK_ORDER and tuple(seen[10:]) == C.EXT_ORDER and pos == len(data)


def test_extension_counts_must_match():
    d = _data(True)
    d.sources.append(C.ColliderSource())
    with pytest.raises(C.CollisionError, match="CSRC"):
        C.read_collision(C.write_collision(d))


def test_extension_ranges_are_checked():
    d = _data(True)
    d.sources[0].set = 9
    with pytest.raises(C.CollisionError, match="set or trace use"):
        C.read_collision(C.write_collision(d))
    d = _data(True)
    d.cylinder_ext[0].kind = 7
    with pytest.raises(C.CollisionError, match="kind"):
        C.read_collision(C.write_collision(d))


def test_string_table_keeps_first_use_order():
    d = C.CollisionData()
    assert [d.string(s) for s in ("b", "a", "b", "c", "a")] == [0, 1, 0, 2, 1]
    assert d.strings == ["b", "a", "c"]
