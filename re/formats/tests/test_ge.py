"""GE list decoding on synthetic Level export bytes (no game data)."""

import pytest

from scctformats import ge
from scctformats._env import bytes_reader
from scctformats._writer import Writer, compact

NAMES = ["None", "Level"]


def make_partition():
    ges = [
        ge.GE(0, 8, 0, 0, 0, [(0.0, 0.0, 256.0), (256.0, 0.0, 256.0)]),            # LG
        ge.GE(1, 32, 0, 0, 4, [(512.0, 0.0, 0.0), (512.0, 0.0, 400.0), (522.0, 0.0, 0.0)]),  # PIPE, ID 4
        ge.GE(2, 64, 2, 0, 0, [(900.0, 100.0, 300.0), (1200.0, 100.0, 120.0)]),    # ZL, editor flag
        ge.GE(3, 16, 0, 0, 1, [(-300.0, 0.0, 0.0), (-300.0, 0.0, 300.0), (-310.0, 0.0, 0.0)]),  # LADDER
    ]
    # Split on X at 400: LG and LADDER on the low side, PIPE and ZL on the high side.
    left = ge.Node(ge.NODE_LEAF, first=0, end=2)
    right_low = ge.Node(ge.NODE_LEAF, first=2, end=3)
    right = ge.Node(4, split=50.0, left=right_low, right=ge.Node(ge.NODE_LEAF, first=3, end=4))
    root = ge.Node(2, split=400.0, left=left, right=right)
    pts = [p for g in ges for p in g.points]
    bmin = tuple(min(p[i] for p in pts) for i in range(3))
    bmax = tuple(max(p[i] for p in pts) for i in range(3))
    return ge.GEPartition(4, bmin, bmax, ges, [0, 3, 1, 2], root)


def level_bytes(part, actors=(1, 2, 3), travel=()):
    w = Writer()
    w.name(0)                                   # None: no tagged properties
    w.i32(len(actors)).i32(len(actors))
    for a in actors:
        w.compact(a)
    for s in ("shadowstrike", "", "Entry.sds", ""):
        w.fstring(s)
    w.compact(0).i32(7777).i32(1)               # no options, port, valid
    w.i32(0)                                    # no model A
    w.i32(1).compact(5)                         # model B = export 5
    w.f32(12.5).compact(0)
    for _ in range(16):
        w.compact(0)
    w.compact(len(travel))
    for k, v in travel:
        w.fstring(k).fstring(v)
    ge.write_partition(w, part)
    return w.bytes()


def test_compact_round_trip():
    from scctformats._env import read_compact
    for v in (0, 1, 63, 64, 127, 128, 8191, 8192, 1 << 20, -1, -64, -65, -(1 << 22)):
        data = compact(v)
        got, p = read_compact(data, 0)
        assert got == v and p == len(data)


def test_level_decodes_every_field():
    part = make_partition()
    data = level_bytes(part, travel=[("a", "b")])
    hdr, got = ge.read_level(bytes_reader(data, NAMES))
    assert hdr.actors == [1, 2, 3]
    assert hdr.url["map"] == "Entry.sds" and hdr.url["port"] == 7777
    assert hdr.model_a == 0 and hdr.model_b == 5
    assert hdr.travel == [("a", "b")]
    assert got.version == 4
    assert [g.kind for g in got.ges] == ["LG", "PIPE", "ZL", "LADDER"]
    assert got.ges[1].id == 4 and got.ges[2].flags == ge.GE_FLAG_EDITOR_ADDED
    assert got.ges[1].points[2] == (522.0, 0.0, 0.0)
    assert got.indices == [0, 3, 1, 2]
    assert got.counts()["LG"] == 1 and got.counts()["POLE"] == 0


def test_record_sizes():
    # i32 kind, i32 flags, i32 platform, u8 id, then 12 bytes per point
    w = Writer()
    ge.write_ge(w, ge.GE(0, 8, 0, 0, 0, [(0, 0, 0), (1, 1, 1)]))
    assert len(w.bytes()) == 13 + 24
    w = Writer()
    ge.write_ge(w, ge.GE(0, 32, 0, 0, 0, [(0, 0, 0)] * 3))
    assert len(w.bytes()) == 13 + 36
    # Before version 170 there is no platform, before 174 no ID byte.
    w = Writer()
    ge.write_ge(w, ge.GE(0, 8, 0, 0, 0, [(0, 0, 0), (1, 1, 1)]), version=160)
    assert len(w.bytes()) == 8 + 24
    g = ge.read_ge(bytes_reader(w.bytes()), 0, version=160)
    assert g.platform == 0 and g.id == 0


def test_round_trip_is_byte_identical():
    part = make_partition()
    data = level_bytes(part)
    _, got = ge.read_level(bytes_reader(data, NAMES))
    assert level_bytes(got) == data


def test_activation_rule():
    g0 = ge.GE(0, 8, 0, 0, 0, [])
    g4 = ge.GE(1, 8, 0, 0, 4, [])
    g6 = ge.GE(2, 8, 0, 0, 6, [])
    assert g0.active(0) and g0.active(255)
    assert g4.active(4) and not g4.active(0) and not g4.active(3)
    assert g6.active(2) and g6.active(4) and not g6.active(1)


def test_tree_check_passes_and_catches_wrong_side():
    part = make_partition()
    rep = ge.check_tree(part)
    assert rep["violations"] == 0 and rep["ges_in_tree"] == 4 and rep["points_outside_bounds"] == 0
    part.tree.left, part.tree.right = part.tree.right, part.tree.left
    assert ge.check_tree(part)["violations"] > 0


def test_tree_stats_and_empty_children():
    part = make_partition()
    part.tree.right.right = None                 # an empty child is serialized as type 1
    w = Writer()
    ge.write_tree(w, part.tree)
    t = ge.read_tree(bytes_reader(w.bytes()), 4)
    assert t.right.right is None and t.right.left.type == ge.NODE_LEAF
    stats = ge.GEPartition(4, (0, 0, 0), (0, 0, 0), [], [], t).tree_stats()
    assert stats["empty"] == 1 and stats["leaf"] == 2 and stats["split_X"] == 1 and stats["split_Y"] == 1


def test_bad_data_is_rejected():
    w = Writer().i32(3).i32(0).i32(0).u8(0)       # kind 3 does not exist
    with pytest.raises(ge.GEFormatError):
        ge.read_ge(bytes_reader(w.bytes()), 0)
    w = Writer().i32(16).i32(0).i32(9)            # leaf past the index list
    with pytest.raises(ge.GEFormatError):
        ge.read_tree(bytes_reader(w.bytes()), 4)
    part = make_partition()
    data = level_bytes(part) + b"\0"
    with pytest.raises(ge.GEFormatError):
        ge.read_level(bytes_reader(data, NAMES))


def test_old_partition_version_is_reported():
    w = Writer().i32(3)
    with pytest.raises(ge.GEFormatError, match="older"):
        ge.read_partition(bytes_reader(w.bytes()))


def test_census_table_shape():
    part = make_partition()
    row = {"map": "TestBox", "counts": part.counts(), "ges": [g.to_json(0) for g in part.ges],
           "activation_mask": 0, "activation_mask_stored": False}
    table = ge.census_table([row])
    lines = table.splitlines()
    assert lines[0].startswith("| Map | GEs | POLE")
    assert "| TestBox | 4 | 0 | 0 | 0 | 1 | 1 | 1 | 1 | 2 | 0 (default) |" in table
