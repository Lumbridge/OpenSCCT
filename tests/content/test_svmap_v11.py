"""SVMap 1.1: derived event edges (fire, accept, notify), mission triggers, GE activity and folder validation."""

from __future__ import annotations

import copy

from svmbuild import canonical
from svmbuild.svmap import LATEST_VERSION, derive_event_edges, notify_seconds, with_derived, write_svmap
from svmbuild.validation import validate_svmap
from svmbuild.writers import collision as C


def _gameplay() -> dict:
    actor = lambda i, c, props: {"id": i, "class": c, "props": props, "src": {"export": 1}}  # noqa: E731
    doc = {
        "formatVersion": LATEST_VERSION,
        "map": "Synth",
        "levelInfo": actor("LevelInfo0", "Engine.LevelInfo", {"Tag": "LevelInfo", "GEActivationMask": 4}),
        "mapSettings": {"Story": {"TimeLimit": 10, "SpiesLives": 4, "SentriesLives": 3, "RespawnTime": 10}},
        "actors": [
            actor("Switch0", "SBase.SInterrupteur", {"Tag": "SInterrupteur", "Event": "go"}),
            actor("Crane0", "SBase.SAnimatedMesh", {"Tag": "go", "Mesh": {"$asset": "Anims.CraneMesh"},
                                                    "Animations": ["seq"]}),
            actor("Wall0", "SBase.SGamePlayObject", {
                "Tag": "boom", "DestroyOnEventFrom": [{"$actor": "Crane0"}],
                "DamagedMeshes": [{"Event": "dust", "Percent": 0}, {"Event": "None"}]}),
            actor("Dust0", "Engine.Emitter", {"Tag": "dust"}),
            actor("Shaker0", "SBase.SShaker", {"Tag": "boom", "ShakeEvents": [{"EventSender": {"$actor": "Crane0"}}]}),
            actor("Magic0", "SBase.SMagicEvent", {"Tag": "Magic", "Groups": [
                {"EventGroup": [{"Event": "dust", "Delay": 1.0}, {"Event": "boom", "Delay": 2.0}]}]}),
            actor("Mission0", "SBase.SMission", {"Tag": "SMission", "GameMode": 4, "Triggers": [{"$actor": "Dust0"}],
                                                "Objectives": [{"$actor": "Objective0"}]}),
            actor("Objective0", "SBase.SObjective", {"Tag": "SObjective", "Triggers": [{"$actor": "Switch0"}]}),
        ],
        "animations": {"Anims.CraneMesh": {"animation": "Anims.CraneAnim", "sequences": {"seq": {
            "frames": 451, "rate": 40.0, "notifies": [
                {"time": 0.97223943, "function": "None", "object": "Anims.Notify1", "class": "Engine.AnimNotify_Trigger",
                 "props": {"EventName": "boom"}},
                {"time": 0.1, "function": "None", "object": "Anims.Sound1", "class": "Engine.AnimNotify_Sound",
                 "props": {"Volume": 4.0}}]}}}},
    }
    return with_derived(doc)


def test_event_edges_cover_every_source():
    g = _gameplay()
    edges = [(e["from"], e["via"], e["event"], tuple(e["to"]), e.get("kind")) for e in g["eventEdges"]]
    assert ("Switch0", "Event", "go", ("Crane0",), None) in edges
    assert ("Wall0", "DamagedMeshes[0].Event", "dust", ("Dust0",), None) in edges
    assert ("Wall0", "DestroyOnEventFrom[0]", "boom", ("Crane0",), "accept") in edges
    assert ("Shaker0", "ShakeEvents[0].EventSender", "boom", ("Crane0",), "accept") in edges
    assert ("Magic0", "Groups[0].EventGroup[0].Event", "dust", ("Dust0",), None) in edges
    assert ("Magic0", "Groups[0].EventGroup[1].Event", "boom", ("Wall0", "Shaker0"), None) in edges
    notify = [e for e in g["eventEdges"] if e.get("kind") == "notify"]
    assert len(notify) == 1 and notify[0]["to"] == ["Wall0", "Shaker0"] and notify[0]["seq"] == "seq"
    assert abs(notify[0]["time"] - 10.962) < 1e-3
    # None-valued event names make no edge.
    assert not any(e["via"] == "DamagedMeshes[1].Event" for e in g["eventEdges"])


def test_edges_are_reproducible_from_the_written_json():
    g = _gameplay()
    back = canonical.loads(canonical.dump_file(g))
    assert derive_event_edges(back) == back["eventEdges"]


def test_notify_seconds_is_float32():
    assert notify_seconds(0.910887, 451, 40.0) == notify_seconds(0.910887, 451, 40.0)
    assert abs(notify_seconds(0.910887, 451, 40.0) - 10.27025) < 1e-4
    assert notify_seconds(0.5, 10, 0.0) == 0.0


def test_mission_tree_has_the_mission_triggers():
    tree = _gameplay()["missionTree"]
    assert tree == [{"mission": "Mission0", "gameMode": 4, "triggers": ["Dust0"],
                     "objectives": [{"objective": "Objective0", "triggers": ["Switch0"]}]}]


def test_1_0_documents_keep_the_1_0_rules():
    g = _gameplay()
    g["formatVersion"] = "1.0.0"
    edges = derive_event_edges(g)
    assert [(e["from"], e["via"]) for e in edges] == [("Switch0", "Event")]


def _write(root, gameplay, traversal):
    d = C.CollisionData(sources=[], material_ext=[], cylinder_ext=[])
    d.zones.append(C.Zone(d.string("LevelInfo0"), 0, (0.0, 0.0, 0.0), (0.0, 0.0, 0.0)))
    return write_svmap(root, map_id="Synth", title="Synth",
                       source={"kind": "synthetic", "generator": "tests", "generatorVersion": 1},
                       gameplay=gameplay, traversal=traversal, collision=C.write_collision(d), render={},
                       format_version=LATEST_VERSION)


def _traversal(active=True):
    return {"formatVersion": LATEST_VERSION, "map": "Synth", "activationMask": 4, "ges": [
        {"id": "GE0", "kind": "LG", "points": [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0]], "mask": 4, "platform": 0,
         "flags": 0, "activeAtStart": active, "src": {"kind": "level", "index": 0}}]}


def test_a_1_1_folder_validates(tmp_path):
    _write(tmp_path / "Synth", _gameplay(), _traversal())
    assert validate_svmap(tmp_path / "Synth") == []


def test_stale_derived_data_is_reported(tmp_path):
    g = _gameplay()
    bad = copy.deepcopy(g)
    bad["actors"][0]["props"]["Event"] = "other"
    _write(tmp_path / "A", bad, _traversal(active=False))
    errs = validate_svmap(tmp_path / "A")
    assert any("eventEdges" in e for e in errs)
    assert any("activeAtStart" in e for e in errs)
