"""SteeE converted from the reference install (skipped without it): the known facts of the map and its crane chain.

Reads the install read-only and writes only to pytest's temporary folder.
"""

from __future__ import annotations

import pytest
from svmbuild import canonical
from svmbuild.paths import default_game_root
from svmbuild.validation import validate_svmap
from svmbuild.writers.collision import KINDS, read_collision

GAME = default_game_root()
pytestmark = pytest.mark.skipif(not (GAME / "Packages" / "Maps" / "SteeE.sdc").exists(),
                                reason="reference install not present")


@pytest.fixture(scope="module")
def steee(tmp_path_factory):
    from svmbuild.mapconv.build import build_map
    from svmbuild.sources.classdb import class_db

    out = tmp_path_factory.mktemp("svmap") / "SteeE"
    stats = build_map(GAME, "SteeE", out, class_db(str(GAME)), map_sha256="0" * 64, render=False)
    return out, stats


def _load(root, rel):
    return canonical.loads((root / rel).read_bytes())


def test_steee_validates(steee):
    root, _ = steee
    assert validate_svmap(root) == []


def test_steee_known_facts(steee):
    root, stats = steee
    g = _load(root, "sim/gameplay.json")
    by_class = stats["actors"]["byClass"]
    assert by_class["SBase.SComputerObjectiveTrigger"] == 5
    assert by_class["SBase.SFlag"] == 1 and by_class["SBase.SFlagDropZone"] == 1
    assert g["levelInfo"]["props"]["GEActivationMask"] == 4
    assert g["mapSettings"]["Story"] == {"TimeLimit": 10, "ScoreLimit": 0, "SpiesLives": 4, "RespawnTime": 10,
                                         "SentriesLives": 3, "Handicap": 0, "NumDisksMax": 0, "GameModeId": 5}
    story = next(m for m in g["missionTree"] if m["gameMode"] == 5)
    assert story["minimumObjectives"] == 4 and len(story["objectives"]) == 6
    t = _load(root, "sim/traversal.json")
    kinds = {}
    for ge in t["ges"]:
        kinds[ge["kind"]] = kinds.get(ge["kind"], 0) + 1
    assert len(t["ges"]) == 228 and kinds == {"LG": 220, "PIPE": 6, "ZL": 2}
    assert [ge["id"] for ge in t["ges"] if ge["mask"]] == ["GE153"] and t["activationMask"] == 4


def test_steee_crane_chain(steee):
    root, _ = steee
    g = _load(root, "sim/gameplay.json")
    edges = g["eventEdges"]
    blast = next(e for e in edges if e["from"] == "SInterrupteur28808")
    assert blast["event"] == "blast" and blast["to"][0] == "SAnimatedMesh277"
    notifies = {e["event"]: e for e in edges if e.get("kind") == "notify" and e["from"] == "SAnimatedMesh277"}
    assert notifies["tueletube"]["to"] == ["Mover2064"]
    assert abs(notifies["tueletube"]["time"] - 10.27) < 0.005 and abs(notifies["blast01"]["time"] - 10.962) < 0.005
    assert notifies["blast01"]["to"][0] == "SGamePlayObject261" and notifies["blast01"]["to"][-1] == "SGETrigger271"
    seq = g["animations"]["sqt13_anm.sqt_grue_mesh"]["sequences"]["sqt_grue_seq"]
    assert (seq["frames"], seq["rate"]) == (451, 40.0) and (seq["frames"] - 1) / seq["rate"] == 11.25


def test_steee_collision(steee):
    root, stats = steee
    d = read_collision((root / "sim" / "collision.bin").read_bytes())
    kinds = {KINDS[c.kind] for c in d.colliders}
    assert {"worldBsp", "brush", "meshPoly", "meshHull", "mover", "glass", "gpo"} <= kinds
    wall = [i for i, c in enumerate(d.colliders) if d.name_of(c.actor) == "SGamePlayObject261"]
    assets = {d.strings[d.sources[i].asset] for i in wall}
    assert assets == {"SQU13_STM.Objects.sqt_murkc_avant", "SQU13_STM.Objects.sqt_murkc_apres"}
    started = [i for i in wall if not d.colliders[i].flags & (1 << 7)]
    assert {d.strings[d.sources[i].asset] for i in started} == {"SQU13_STM.Objects.sqt_murkc_avant"}
    assert d.zones[0].actor != 0xFFFFFFFF and d.strings[d.zones[0].actor] == "LevelInfo0"


def test_steee_bsp_collision_bounds(steee):
    # The original LeafHulls: one hull of no collider per world node with a collision bound, its oriented planes
    # holding the node's own polygon on or behind them.
    root, _ = steee
    d = read_collision((root / "sim" / "collision.bin").read_bytes())
    owned = {h for c in d.colliders for h in range(c.hull_start, c.hull_start + c.hull_count)}
    world = next(c for c in d.colliders if KINDS[c.kind] == "worldBsp")
    nodes = d.bsp[world.bsp_start:world.bsp_start + world.bsp_count]
    bounded = [n for n in nodes if n.collision_bound >= 0]
    assert len(nodes) == 1624 and len(bounded) == 586
    assert all(n.back == -1 for n in bounded)
    assert not any(n.collision_bound in owned for n in d.bsp if n.collision_bound >= 0)
    worst = 0.0
    for n in bounded:
        h = d.hulls[n.collision_bound]
        planes = d.planes[h.plane_start:h.plane_start + h.plane_count]
        assert h.vert_count == 0 and len(planes) >= 2
        for t in d.tris[n.tri_start:n.tri_start + n.tri_count]:
            for v in t[:3]:
                p = d.verts[v]
                worst = max(worst, max(pl[0] * p[0] + pl[1] * p[1] + pl[2] * p[2] - pl[3] for pl in planes))
    assert worst < 0.25  # float32 rounding of the original planes: 0.09 uu at most on SteeE
