from __future__ import annotations

import struct

import pytest
from svmbuild.writers import collision as C


def _sample() -> C.CollisionData:
    d = C.CollisionData()
    m = len(d.materials)
    d.materials.append(C.Material(3, C.flag_bits(["climbable", "invisible"], C.MATERIAL_FLAGS), d.string("Pkg.Mat")))
    d.verts += [(0.0, 0.0, 0.0), (100.0, 0.0, 0.0), (0.0, 100.0, 0.0), (0.5, -1.25, 1e-7)]
    d.tris.append((0, 1, 2, 0, m))
    d.planes += [(0.0, 0.0, 1.0, 0.0), (1.0, 0.0, 0.0, 4.0)]
    d.hulls.append(C.Hull(0, 2, 3, 1, m, (-1.0, -2.0, -3.0), (1.0, 2.0, 3.0)))
    d.bsp.append(C.BspNode((0.0, 0.0, 1.0, 0.0), front=-1, back=-1, coplanar=-1, collision_bound=0, tri_start=0,
                           tri_count=1, node_flags=5, zone_front=1, zone_back=0))
    d.colliders.append(C.Collider(kind=0, flags=63, vert_start=0, vert_count=3, tri_start=0, tri_count=1,
                                  bsp_start=0, bsp_count=1, aabb_min=(0.0, 0.0, 0.0), aabb_max=(100.0, 100.0, 0.0)))
    d.colliders.append(C.Collider(kind=4, flags=C.flag_bits(["collideActors", "startsDisabled"], C.COLLIDER_FLAGS),
                                  actor=d.string("Mover7"), parent=d.string("Baseé"), hull_start=0, hull_count=1,
                                  location=(1.0, 2.0, 3.0), rotation=(-16384, 70000, 5), scale=(2.0, 2.0, 0.5),
                                  pre_pivot=(0.0, 0.0, 8.0), aabb_min=(-5.0, -5.0, -5.0), aabb_max=(5.0, 5.0, 5.0)))
    d.cylinders.append(C.Cylinder(d.string("Trigger3"), 1, (10.0, 20.0, 30.0), 40.0, 50.0))
    d.zones.append(C.Zone(C.NONE, 0, (0.0, 0.0, 0.0), (1.0, 1.0, 1.0)))
    d.zones.append(C.Zone(d.string("ZoneInfo2"), 1, (-1.0, -1.0, -1.0), (2.0, 2.0, 2.0)))
    return d


def _chunks(blob: bytes):
    pos = 16
    out = []
    for _ in range(struct.unpack_from("<I", blob, 8)[0]):
        cc, version, size, count = struct.unpack_from("<4sIII", blob, pos)
        out.append((pos, cc, version, size, count))
        pos = (pos + 16 + size + 15) & ~15
    return out, pos


def test_round_trip_sample():
    blob = C.write_collision(_sample())
    d = C.read_collision(blob)
    assert C.write_collision(d) == blob
    assert d.strings == ["Pkg.Mat", "Mover7", "Baseé", "Trigger3", "ZoneInfo2"]
    assert d.colliders[1].rotation == (-16384, 70000, 5)
    assert d.verts[3] == (0.5, -1.25, struct.unpack("<f", struct.pack("<f", 1e-7))[0])
    assert C.flag_names(d.materials[0].flags, C.MATERIAL_FLAGS) == ["climbable", "invisible"]
    assert C.flag_names(d.colliders[1].flags, C.COLLIDER_FLAGS) == ["collideActors", "startsDisabled"]


def test_round_trip_testbox(testbox_dir):
    blob = (testbox_dir / "sim" / "collision.bin").read_bytes()
    assert C.write_collision(C.read_collision(blob)) == blob


def test_layout_and_alignment(testbox_dir):
    blob = (testbox_dir / "sim" / "collision.bin").read_bytes()
    magic, major, minor, count, flags = struct.unpack_from("<4sHHII", blob, 0)
    assert (magic, major, minor, count, flags) == (b"SVCL", 1, 0, 10, 0)
    chunks, end = _chunks(blob)
    assert end == len(blob) and len(blob) % 16 == 0
    assert [c[1] for c in chunks] == list(C.CHUNK_ORDER)
    strides = {b"VERT": 12, b"TRIS": 16, b"TFLG": 8, b"PLNS": 16, b"HULL": 48, b"BSPN": 48, b"COLL": 128,
               b"CYLS": 32, b"ZONE": 32}
    for pos, cc, version, size, n in chunks:
        assert pos % 16 == 0 and version == 1
        if cc in strides:
            assert size == n * strides[cc]


def test_coll_record_offsets():
    d = _sample()
    blob = C.write_collision(d)
    chunks, _ = _chunks(blob)
    pos = next(p for p, cc, *_ in chunks if cc == b"COLL") + 16 + 128   # second collider
    assert blob[pos] == 4                                                   # kind mover
    assert struct.unpack_from("<H", blob, pos + 2)[0] == (1 | 1 << 7)      # flags
    assert struct.unpack_from("<II", blob, pos + 4) == (1, 2)              # actor, parent string indices
    assert struct.unpack_from("<II", blob, pos + 40) == (0, 1)             # hullStart, hullCount
    assert struct.unpack_from("<3f", blob, pos + 48) == (1.0, 2.0, 3.0)    # location
    assert struct.unpack_from("<3i", blob, pos + 60) == (-16384, 70000, 5)  # rotation
    assert struct.unpack_from("<3f", blob, pos + 72) == (2.0, 2.0, 0.5)    # scale
    assert struct.unpack_from("<3f", blob, pos + 84) == (0.0, 0.0, 8.0)    # prePivot
    assert struct.unpack_from("<3f", blob, pos + 108) == (5.0, 5.0, 5.0)   # aabbMax


def test_unknown_chunk_is_skipped_and_duplicate_rejected():
    original = C.write_collision(_sample())
    chunks, _ = _chunks(original)
    pos, cc, version, size, n = chunks[-1]
    last_chunk = original[pos:pos + ((16 + size + 15) & ~15)]
    blob = bytearray(original)
    struct.pack_into("<I", blob, 8, 11)
    extra = struct.pack("<4sIII", b"XTRA", 7, 5, 1) + b"hello" + bytes(11)
    for strict in (False, True):
        d = C.read_collision(bytes(blob) + extra, strict=strict)
        assert d.unknown == [(b"XTRA", 7, 1, b"hello")]
        assert len(d.colliders) == 2
    with pytest.raises(C.CollisionError):
        C.read_collision(bytes(blob) + last_chunk, strict=False)


@pytest.mark.parametrize("mutate", ["magic", "major", "chunk_version", "size", "padding", "tri_owner", "nan"])
def test_rejects_bad_files(mutate):
    d = _sample()
    if mutate == "tri_owner":
        d.colliders[0].tri_count = 0
        with pytest.raises(C.CollisionError):
            C.read_collision(C.write_collision(d))
        return
    if mutate == "nan":
        d.verts[0] = (float("nan"), 0.0, 0.0)
        with pytest.raises(C.CollisionError):
            C.write_collision(d)
        return
    blob = bytearray(C.write_collision(d))
    chunks, _ = _chunks(bytes(blob))
    if mutate == "magic":
        blob[0:4] = b"XXXX"
    elif mutate == "major":
        struct.pack_into("<H", blob, 4, 2)
    elif mutate == "chunk_version":
        struct.pack_into("<I", blob, chunks[1][0] + 4, 2)
    elif mutate == "size":
        struct.pack_into("<I", blob, chunks[1][0] + 12, chunks[1][4] + 1)
    elif mutate == "padding":
        pos, cc, version, size, n = chunks[0]   # STRS ends unaligned in the sample
        end = pos + 16 + size
        assert end % 16
        blob[end] = 1
    with pytest.raises(C.CollisionError):
        C.read_collision(bytes(blob))
