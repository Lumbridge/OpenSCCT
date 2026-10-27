"""Checks of the TestRange layout, its world BSP and the tracked manifest (no game needed).

    python -B -m pytest tools/testmap -q
"""

from __future__ import annotations

import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parents[1] / "content"))

import bsp as B  # noqa: E402
import generate as G  # noqa: E402
import layout as L  # noqa: E402
from svmbuild import canonical  # noqa: E402


def _truth(carves, solids, p) -> bool:
    def inside(c):
        return all(pl.dist(p) < 0 for pl in c.planes)

    return any(inside(c) for c in carves) and not any(inside(s) for s in solids)


def test_ids_are_unique_and_stations_name_known_ges():
    ids = [g.id for g in L.GES]
    assert len(ids) == len(set(ids))
    assert len({s.id for s in L.STATIONS}) == len(L.STATIONS)
    assert len({a.id for a in L.ACTORS}) == len(L.ACTORS)


def test_world_bsp_classifies_points_as_the_volumes_do():
    carves, solids = G.world_volumes()
    polys = G.world_polys()
    room = next(z for z in L.ZONES if z.id == "ZoneRoom")
    root = B.build(polys, [(1, tuple(c - 16 for c in room.lo), tuple(c + 16 for c in room.hi))], 2)
    rng = random.Random(7)
    bad = []
    for _ in range(20000):
        p = (rng.uniform(-100, 8300), rng.uniform(-100, 6250), rng.uniform(-60, 1100))
        if B.classify_point(root, p) != _truth(carves, solids, p):
            bad.append(p)
    assert not bad, bad[:5]


def test_boundary_faces_the_empty_side():
    carves, solids = G.world_volumes()
    for poly in G.world_polys():
        c = poly.centroid()
        front = tuple(c[i] + poly.plane.n[i] * 0.5 for i in range(3))
        back = tuple(c[i] - poly.plane.n[i] * 0.5 for i in range(3))
        assert _truth(carves, solids, front), (poly.material, c)
        assert not _truth(carves, solids, back), (poly.material, c)


def test_tracked_manifest_is_current(tmp_path):
    _, stations = G.generate(tmp_path / L.MAP_ID, with_classes=False)
    assert G.MANIFEST.read_bytes() == canonical.dump_file(stations), "run python -B tools/testmap/generate.py"


def test_zip_line_numbers():
    s = next(s for s in L.STATIONS if s.id == "Zip1760")
    hx, hy, hz = s.data["high"]
    lx, ly, lz = s.data["low"]
    assert (hx - lx, hy - ly) == (0.0, 1760.0)
    assert hz - lz == 320.0
    assert hz - 384.0 == 304.0            # over the deck it starts from
    assert lz - s.data["landingTop"] == 208.0
