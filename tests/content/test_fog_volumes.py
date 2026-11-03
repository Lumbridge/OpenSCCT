"""Synthetic convex brush decomposition, open-face sampling and native height bounds."""
from types import SimpleNamespace
import numpy as np
from svmbuild.mapconv.fog import brush_pieces, convex_vertices, point_region, volume_entries, fallback_box


def cube(size=100.0):
    return np.array([[1, 0, 0, size], [-1, 0, 0, size], [0, 1, 0, size],
                     [0, -1, 0, size], [0, 0, 1, size], [0, 0, -1, size]])


def brush():
    nodes = [SimpleNamespace(plane=p, num_verts=4, flags=0, front=-1, back=i + 1 if i < 5 else -1,
                             zone_front=1, zone_back=0) for i, p in enumerate(cube())]
    return SimpleNamespace(root_outside=True, nodes=nodes)


def test_brush_inside_leaves_transform_planes_with_nonuniform_scale():
    matrix = np.diag([2.0, 3.0, .5])
    offset = np.array([30.0, -10.0, 20.0])
    pieces = brush_pieces(brush(), matrix, offset)
    assert len(pieces) == 1
    vertices = convex_vertices(pieces[0])
    assert len(vertices) == 8
    np.testing.assert_allclose(vertices.min(axis=0), offset - [200, 300, 50])
    np.testing.assert_allclose(vertices.max(axis=0), offset + [200, 300, 50])
    np.testing.assert_allclose(np.linalg.norm(pieces[0][:, :3], axis=1), 1)


def test_solid_leaf_is_not_a_valid_fog_zone():
    model = brush()
    assert point_region(model, np.array([0, 0, 0])) == 255
    assert point_region(model, np.array([101, 0, 0])) == 1


def test_open_faces_floor_sampling_and_height_cap_preserve_original_planes():
    world_zone = lambda p: 1 if p[2] >= 0 else 255
    entries = volume_entries([cube()], [1, .5, 0], 300, 1, 64, 1, world_zone)
    assert len(entries) == 1
    entry = entries[0]
    assert entry["floor"] == 5  # min Z + 1, then steps of 8 until above the floor.
    assert entry["extinction"] == .01
    assert entry["openPlanes"] == 5
    assert entry["planes"][-1] == [0, 0, -1, 100]  # floor-facing boundary is closed.
    assert [0, 0, 1, 100] in entry["planes"]
    assert entry["max"][2] == 17  # floor + ceil(log2(diagonal * extinction * 1024)).


def test_adjacent_convex_pieces_do_not_fade_at_their_shared_face():
    first, second = cube(), cube()
    first[0, 3] = 0
    second[1, 3] = 0
    entries = volume_entries([first, second], [1, 1, 1], 100, 0, 32, -1, lambda p: 1)
    assert len(entries) == 2
    assert all(e["openPlanes"] == 5 for e in entries)
    assert entries[0]["planes"][-1] == [1, 0, 0, 0]
    assert entries[1]["planes"][-1] == [-1, 0, 0, 0]


def test_no_open_inside_produces_no_fog():
    assert volume_entries([cube()], [1, 1, 1], 100, 0, 64, -1, lambda p: 255) == []


def test_fallback_finds_offset_inside_and_bisects_box_extents():
    pieces = fallback_box(np.zeros(3), lambda p: 100 < p[0] < 300 and abs(p[1]) < 100 and abs(p[2]) < 100)
    assert len(pieces) == 1
    points = convex_vertices(pieces[0])
    np.testing.assert_allclose(points.min(axis=0), [100, -100, -100], atol=.004)
    np.testing.assert_allclose(points.max(axis=0), [300, 100, 100], atol=.004)
    assert fallback_box(np.zeros(3), lambda p: False) == []


def test_new_volume_class_is_render_only_without_simulation_metadata():
    from svmbuild.mapconv.source import ActorInfo, MapSource
    source = MapSource.__new__(MapSource)
    source.db = SimpleNamespace(default=lambda cls, name, default=None: default)
    actor = ActorInfo(1, 2, "NewFog", "SBase.SFogVolume", {})
    source.classify(actor)
    assert actor.category == "render"
    for props in ({"bDeleteMe": True}, {"bPendingDelete": True}, {"Platform": 1}):
        skipped = ActorInfo(1, 2, "SkippedFog", "SBase.SFogVolume", props)
        source.classify(skipped)
        assert skipped.category == "skipped"
