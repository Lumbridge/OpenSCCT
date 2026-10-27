"""Checks of the Hub layout, its world BSP and the tracked folder and manifest (no game needed).

    python -B -m pytest tools/hubmap -q
"""

from __future__ import annotations

import importlib.util
import math
import random
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def _load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


G = _load("hub_generate", HERE / "generate.py")
L = G.L
B = G.B


def _truth(carves, solids, p) -> bool:
    def inside(c):
        return all(pl.dist(p) < 0 for pl in c.planes)

    return any(inside(c) for c in carves) and not any(inside(s) for s in solids)


def test_world_bsp_classifies_points_as_the_volumes_do():
    carves, solids = G.world_volumes()
    root = B.build(G.world_polys(), [], 1)
    rng = random.Random(11)
    bad = []
    for _ in range(20000):
        p = (rng.uniform(-100, 12400), rng.uniform(-100, 12400), rng.uniform(-60, 1600))
        if B.classify_point(root, p) != _truth(carves, solids, p):
            bad.append(p)
    assert not bad, bad[:5]


def test_starts_stand_on_open_floor_apart_from_each_other():
    carves, solids = G.world_volumes()
    starts = [a for a in L.ACTORS if a.cls == "Engine.PlayerStart"]
    assert len(starts) == 64
    assert sum(1 for a in starts if a.props["TeamNumber"] == 0) == 32
    for a in starts:
        x, y, z = a.location
        assert z == L.START_Z
        # Open space for a standing pawn (radius 35, 86 half height round the start), floor under it.
        for dx, dy in ((0, 0), (40, 0), (-40, 0), (0, 40), (0, -40)):
            for dz in (-80, 0, 80):
                assert _truth(carves, solids, (x + dx, y + dy, z + dz)), (a.id, dx, dy, dz)
        assert not _truth(carves, solids, (x, y, -1.0))
    for i, a in enumerate(starts):
        for b in starts[i + 1:]:
            assert math.dist(a.location[:2], b.location[:2]) >= 100.0, (a.id, b.id)


def test_play_spaces_are_clear_floor():
    """The pad courts (where players queue) and the aim range's lane are open floor; dressing stays round them."""
    carves, solids = G.world_volumes()
    spaces = []
    for area in L.AREAS:
        if area.kind == "matchPads":
            spaces += [(p["id"], p["lo"], p["hi"]) for p in area.data["pads"]]
        if area.kind == "aimRange":
            spaces.append((area.id, (area.data["backstop"] + 1, area.data["lane"][0]),
                           (area.data["firingLine"] - 65, area.data["lane"][1])))
    assert len(spaces) == 5
    for ident, (x0, y0), (x1, y1) in spaces:
        rng = random.Random(ident)
        for _ in range(400):
            p = (rng.uniform(x0, x1), rng.uniform(y0, y1), 40.0)
            assert _truth(carves, solids, p), (ident, p)


def test_every_pad_has_every_theme_kit_and_a_sign_anchor():
    pads = next(a for a in L.AREAS if a.kind == "matchPads").data["pads"]
    for pad in pads:
        assert pad["theme"] in L.THEMES
        assert len(pad["signAnchor"]) == 3
        for theme in L.THEMES:
            assert f"HubTheme_{pad['id']}_{theme}" in L.GROUPS


def test_zip_line_matches_testrange():
    hx, hy, hz = L.ZIP_HIGH
    lx, ly, lz = L.ZIP_LOW
    assert (hx - lx, hy - ly) == (0.0, 1760.0)
    assert hz - lz == 320.0
    assert hz - 384.0 == 304.0
    assert lz - 160.0 == 208.0


def test_no_stock_names_in_the_folder():
    folder = G.DEFAULT_OUT
    for p in folder.rglob("*"):
        if p.suffix in (".json",):
            text = p.read_text(encoding="utf-8")
            assert "_TXT" not in text and "_txt" not in text, p


def test_tracked_folder_and_manifest_are_current():
    assert G.check(G.DEFAULT_OUT) == [], "run python -B tools/hubmap/generate.py"
