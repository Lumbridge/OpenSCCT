"""Static mesh collision tail decoding on synthetic bytes (no game data)."""

import numpy as np
import pytest

from scctformats import smcollision as sc
from scctformats._env import bytes_reader
from scctformats._writer import Writer

BOX = [(-1, -1, -1), (1, -1, -1), (1, 1, -1), (-1, 1, -1), (-1, -1, 1), (1, -1, 1), (1, 1, 1), (-1, 1, 1)]
# Outward-facing quads, each split into two triangles wound clockwise seen from outside (UE convention).
QUADS = [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)]


def box_tris(drop_top=False):
    tris = []
    for q in QUADS:
        if drop_top and q == (4, 7, 6, 5):
            continue
        a, b, c, d = q
        tris += [(a, b, c), (a, c, d)]
    return tris


def plane_of(p, t):
    a, b, c = (np.array(p[i], float) for i in t)
    n = np.cross(c - a, b - a)          # outward for the winding above
    n /= np.linalg.norm(n)
    return (*n, float(n @ a))


def write_raw_tri(w, verts, nuv=1, mat=0):
    for v in verts:
        w.vec3(v)
    w.i32(nuv)
    for _ in range(max(0, nuv)):
        for _ in range(6):
            w.f32(0.5)
    for _ in range(3):
        w.raw(bytes([10, 20, 30, 255]))
    w.i32(mat).u32(1)


def write_bsp(w, nodes, version):
    w.compact(len(nodes))
    for n in nodes:
        for x in n[:4]:
            w.compact(x)
        if version >= 0x9D and version != 0x9E:
            w.u32(0xFFFE)


def collision_bytes(version=300, base=0, drop_top=False, hull_nuv=0):
    tris = box_tris(drop_top)
    w = Writer()
    wire = [0, 1, 1, 2]
    w.compact(len(wire))
    for i in wire:
        w.u16(i)
    w.i32(1)
    # per-poly: one triangle over render vertices 0, 1, 2
    w.compact(1).f32(0).f32(0).f32(-1).f32(1)
    w.compact(1).u16(0).u16(1).u16(2).u16(0)
    write_bsp(w, [(0, -1, -1, -1)], version)
    # hull: the box
    w.compact(len(BOX))
    for v in BOX:
        w.vec3(v).vec3(np.array(v, float) / np.sqrt(3))
    w.i32(2)
    w.compact(len(tris))
    for t in tris:
        w.f32(0).f32(0).f32(0).f32(0)            # patched below with real planes
    planes_at = None
    data = bytearray(w.bytes())
    # rewrite planes properly (simpler than computing the offset up front)
    w = Writer().raw(bytes(data[:len(data) - 16 * len(tris)]))
    for t in tris:
        nx, ny, nz, d = plane_of(BOX, t)
        w.f32(nx).f32(ny).f32(nz).f32(d)
    w.compact(len(tris))
    for t in tris:
        w.u16(t[0]).u16(t[1]).u16(t[2]).u16(0)
    # a trivial chain: each node holds one triangle, the next one in its back subtree
    nodes = [(i, -1, i + 1 if i + 1 < len(tris) else -1, -1) for i in range(len(tris))]
    write_bsp(w, nodes, version)
    # hull source triangles (lazy array)
    head_at = len(w.bytes())
    w.i32(0)
    w.compact(len(tris))
    for t in tris:
        write_raw_tri(w, [BOX[i] for i in t], nuv=hull_nuv, mat=-1)
    end1 = base + len(w.bytes())
    data = bytearray(w.bytes())
    if version < 0xAF:
        data[head_at:head_at + 4] = end1.to_bytes(4, "little")
    w = Writer().raw(bytes(data))
    w.compact(2).u16(0).u16(1).i32(3)         # hull edge index buffer
    head2 = len(w.bytes())
    w.i32(0)
    w.compact(1)
    write_raw_tri(w, [(0, 0, 0), (1, 0, 0), (0, 1, 0)], nuv=2)
    end2 = base + len(w.bytes())
    data = bytearray(w.bytes())
    if version < 0xAF:
        data[head2:head2 + 4] = end2.to_bytes(4, "little")
    w = Writer().raw(bytes(data))
    w.i32(8)
    if version >= 0x78:
        w.i32(0)
    return w.bytes(), tris


@pytest.mark.parametrize("version", [172, 175, 300])
def test_collision_tail_decodes(version):
    data, tris = collision_bytes(version)
    c = sc.read_collision(bytes_reader(data), version)
    assert len(c.wire_indices) == 4
    assert c.pp_tris.tolist() == [[0, 1, 2, 0]]
    assert c.pp_bsp[0, :4].tolist() == [0, -1, -1, -1]
    assert len(c.hull_verts) == 8 and c.hull_revision == 2
    assert len(c.hull_tris) == len(c.hull_planes) == 12
    assert len(c.hull_source) == 12 and c.hull_source[0].material == -1
    assert len(c.raw_tris) == 1 and len(c.raw_tris[0].uvs) == 2
    assert c.raw_tris[0].colors[0] == (30, 20, 10, 255)
    assert c.internal_version == 8
    assert c.extra == (0 if version >= 0x78 else None)


def test_old_lazy_arrays_point_past_their_data():
    base = 5000
    data, _ = collision_bytes(172, base=base)
    r = bytes_reader(data)
    r.base = base
    c = sc.read_collision(r, 172)
    assert c.lazy_heads == c.lazy_ends


def test_negative_numuvs_means_no_uv_data():
    data, _ = collision_bytes(300, hull_nuv=-12345)
    c = sc.read_collision(bytes_reader(data), 300)
    assert all(t.uvs == [] for t in c.hull_source)


def test_trailing_bytes_are_an_error():
    data, _ = collision_bytes(300)
    with pytest.raises(sc.SMFormatError):
        sc.read_collision(bytes_reader(data + b"\0"), 300)


def test_old_versions_are_refused():
    with pytest.raises(sc.SMFormatError):
        sc.read_collision(bytes_reader(b""), 0xA5)


def test_closed_box_hull_checks():
    data, _ = collision_bytes(300)
    c = sc.read_collision(bytes_reader(data), 300)
    hulls = sc.hulls_of(c)
    assert len(hulls) == 1
    h = hulls[0]
    assert h["tris"] == 12 and h["verts"] == 8 and h["euler_ok"] and h["closed"] and h["oriented"] and h["convex"]
    assert h["plane_side"] == "inside<=0" and h["in_bsp"] == 12
    pc = sc.check_planes(c.hull_planes, c.hull_verts[:, :3], c.hull_tris)
    assert pc["normal_agree"] == 12 and pc["sign"] == "flipped" and pc["dist_max"] < 1e-5
    assert sc.check_bsp_sides(c.hull_bsp, c.hull_verts[:, :3], c.hull_tris, c.hull_planes)["ok"]


def test_open_box_hull_is_flagged_and_capping_closes_it():
    data, tris = collision_bytes(300, drop_top=True)
    c = sc.read_collision(bytes_reader(data), 300)
    h = sc.hulls_of(c)[0]
    assert h["tris"] == 10 and not h["closed"] and not h["euler_ok"]
    assert h["open_edges"] == 4 and h["open_loops"] == [4] and h["planar_loops"] == 1
    assert h["euler_ok_if_capped"] and h["convex"]


def test_bsp_side_check_catches_a_wrong_child():
    data, _ = collision_bytes(300)
    c = sc.read_collision(bytes_reader(data), 300)
    bsp = c.hull_bsp.copy()
    # node 0 is the bottom face (z = -1, outward normal -z); put the top face (wholly behind it? no: in front
    # of nothing) in its coplanar slot: it is not in the plane, so the check must fail.
    top = next(i for i, t in enumerate(c.hull_tris[:, :3].tolist()) if set(t) <= {4, 5, 6, 7})
    bsp[0, 1] = top
    bsp[top, 0] = top
    rep = sc.check_bsp_sides(bsp, c.hull_verts[:, :3], c.hull_tris, c.hull_planes)
    assert not rep["ok"] and rep["violations"].get("coplanar") == 1


def test_bsp_stats():
    bsp = np.array([[0, -1, 1, -1, 0], [1, -1, -1, 5, 0]])
    s = sc.bsp_stats(bsp, 3)
    assert s["nodes"] == 2 and s["tris_in_tree"] == 2 and s["bad_children"] == 1 and not s["capped"]


def test_per_poly_matches_render_tris():
    pos = np.array([(0, 0, 0), (1, 0, 0), (0, 1, 0), (1, 1, 0)], np.float32)
    idx = np.array([0, 1, 2, 2, 1, 3], np.uint32)
    planes = np.array([[0, 0, -1, 0], [0, 0, -1, 0], [0, 0, -1, 0]], np.float32)
    # per-poly keeps a two-sided duplicate of the first triangle (same corners, other winding)
    pp = np.array([[0, 1, 2, 0], [2, 1, 0, 0], [2, 1, 3, 0]], np.uint16)
    col = sc.Collision(np.zeros(0, np.uint16), planes, pp, np.zeros((0, 5)), np.zeros((0, 6), np.float32), 0,
                       np.zeros((0, 4), np.float32), np.zeros((0, 4), np.uint16), np.zeros((0, 5)), [],
                       np.zeros(0, np.uint16), [], 8, 0)
    m = sc.MeshData("t", 300, {}, ((0, 0, 0), (1, 1, 0), 1), (0, 0, 0, 1),
                    [sc.Section(0, 0, 0, 3, 0, 2)], pos, np.zeros_like(pos), idx, col)
    rep = sc.check_per_poly(m, [True])
    assert rep["render_not_in_pp"] == 0 and rep["pp_shape_not_in_render"] == 0
    assert rep["pp_duplicate_corner_sets"] == 1 and rep["pp_matched_render"] == 2
    assert rep["plane_normal_agree"] == 3
    # with collision off for the only section nothing is expected
    rep = sc.check_per_poly(m, [False])
    assert rep["render_tris_collision_enabled"] == 0 and rep["pp_shape_not_in_render"] == 2
