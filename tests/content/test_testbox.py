from __future__ import annotations

import copy
import json

import numpy as np
import pytest
from svmbuild import canonical, testbox
from svmbuild.validation import gameplay_errors, schema_errors, traversal_errors, validate_svmap
from svmbuild.writers.collision import KINDS, read_collision
from svmbuild.writers.glb import accessor_array, read_glb


def _load(root, rel):
    return canonical.loads((root / rel).read_bytes())


def test_fixture_is_valid(testbox_dir):
    assert validate_svmap(testbox_dir) == []


@pytest.mark.parametrize("kind,rel", [("manifest", "manifest.json"), ("gameplay", "sim/gameplay.json"),
                                      ("traversal", "sim/traversal.json")])
def test_schema(testbox_dir, kind, rel):
    assert schema_errors(kind, _load(testbox_dir, rel)) == []


def test_generation_is_byte_identical(tmp_path):
    a, b = tmp_path / "a", tmp_path / "b"
    testbox.generate(a)
    testbox.generate(b)
    files_a = sorted(p.relative_to(a).as_posix() for p in a.rglob("*") if p.is_file())
    files_b = sorted(p.relative_to(b).as_posix() for p in b.rglob("*") if p.is_file())
    assert files_a == files_b == ["manifest.json", "render/world.glb", "sim/collision.bin", "sim/gameplay.json",
                                  "sim/traversal.json"]
    for rel in files_a:
        assert (a / rel).read_bytes() == (b / rel).read_bytes(), rel


def test_committed_fixture_is_current(testbox_dir):
    assert testbox.check(testbox_dir) == []


def test_manifest_hashes_recompute(testbox_dir):
    manifest = _load(testbox_dir, "manifest.json")
    files, hashes = canonical.hash_map(testbox_dir)
    assert manifest["files"] == files
    assert manifest["hashes"] == hashes
    # The sim hash by hand, as the C# reader computes it.
    rows = []
    for rel in sorted(["sim/collision.bin", "sim/gameplay.json", "sim/traversal.json"]):
        data = (testbox_dir / rel).read_bytes()
        if rel.endswith(".json"):
            data = canonical.canonical_bytes(data)
        rows.append(f"{canonical.sha256_hex(data)}  {rel}\n")
    assert canonical.sha256_hex("".join(rows).encode("utf-8")) == manifest["hashes"]["sim"]


def test_gameplay_content(testbox_dir):
    g = _load(testbox_dir, "sim/gameplay.json")
    by_id = {a["id"]: a for a in g["actors"]}
    assert g["levelInfo"]["props"]["GEActivationMask"] == 4
    assert g["mapSettings"]["Story"] == {"TimeLimit": 10, "SpiesLives": 4, "SentriesLives": 3, "RespawnTime": 10}
    starts = [a for a in g["actors"] if a["class"] == "Engine.PlayerStart"]
    assert sorted(a["props"]["TeamNumber"] for a in starts) == [0, 0, 1, 1]
    terminals = [a for a in g["actors"] if a["class"] == "SBase.SComputerObjectiveTrigger"]
    assert len(terminals) == 2 and all(t["props"]["DominationCaptureTime"] == 10 for t in terminals)
    assert by_id["SFlag0"]["props"]["DropZone"] == {"$actor": "SFlagDropZone0"}
    assert by_id["Mover0"]["props"]["InitialState"] == "TriggerOpenTimed"
    mission = by_id["SMission0"]["props"]
    assert mission["GameMode"] == 5 and mission["MinimumObjectives"] == 2
    assert g["missionTree"][0]["objectives"][2] == {"objective": "SObjective2", "triggers": ["SFlag0"]}
    assert {e["from"]: e["to"] for e in g["eventEdges"]} == {"Trigger0": ["Mover0"], "Trigger1": ["SGETrigger0"]}


def test_traversal_content(testbox_dir):
    t = _load(testbox_dir, "sim/traversal.json")
    kinds = [ge["kind"] for ge in t["ges"]]
    assert kinds.count("LG") == 2 and kinds.count("ZL") == 1
    assert {ge["id"]: ge["mask"] for ge in t["ges"]} == {"GE0": 0, "GE1": 4, "GE2": 0}


def test_stairs_and_ramp(testbox_dir):
    d = read_collision((testbox_dir / "sim" / "collision.bin").read_bytes())
    v = np.array(d.verts[: d.colliders[0].vert_count], dtype=np.float64)
    tris = np.array([t[:3] for t in d.tris])
    a, b, c = v[tris[:, 0]], v[tris[:, 1]], v[tris[:, 2]]
    n = np.cross(b - a, c - a)
    n /= np.linalg.norm(n, axis=1)[:, None]
    mid = (a + b + c) / 3
    step_tops = sorted({round(float(mid[i, 2]), 3) for i in range(len(tris))
                        if n[i, 2] > 0.999 and 2304 < mid[i, 0] < 2496 and 448 < mid[i, 1] < 832})
    assert step_tops == [35.0 * k for k in range(1, 9)]
    slopes = [float(np.degrees(np.arccos(n[i, 2]))) for i in range(len(tris)) if 0.5 < n[i, 2] < 0.99]
    assert slopes and all(abs(s - 30.0) < 1e-3 for s in slopes)


def test_collision_colliders(testbox_dir):
    d = read_collision((testbox_dir / "sim" / "collision.bin").read_bytes())
    kinds = [KINDS[c.kind] for c in d.colliders]
    assert kinds == ["worldBsp", "mover", "brush"]
    volume = d.colliders[2]
    assert d.name_of(volume.actor) == "BlockingVolume0" and volume.rotation == (0, 16384, 0)
    # Yaw 16384 turns the local 192 x 64 footprint into a 64 x 192 one.
    assert volume.aabb_min == (2496.0, 832.0, 280.0) and volume.aabb_max == (2560.0, 1024.0, 512.0)
    assert [d.name_of(z.actor) for z in d.zones] == ["LevelInfo0", "ZoneInfo0", "ZoneInfo1", "ZoneInfo2"]
    assert len(d.cylinders) == 6


def test_glb_winding_and_space(testbox_dir):
    g, binary = read_glb((testbox_dir / "render" / "world.glb").read_bytes())
    for mesh in g["meshes"]:
        prim = mesh["primitives"][0]
        pos = accessor_array(g, binary, prim["attributes"]["POSITION"])
        nrm = accessor_array(g, binary, prim["attributes"]["NORMAL"])
        idx = accessor_array(g, binary, prim["indices"]).reshape(-1, 3)
        a, b, c = pos[idx[:, 0]], pos[idx[:, 1]], pos[idx[:, 2]]
        # glTF front faces are counter-clockwise: the right-hand normal matches the stored normal.
        assert ((np.cross(b - a, c - a) * nrm[idx[:, 0]]).sum(axis=1) > 0).all(), mesh["name"]
    world = g["meshes"][0]["primitives"][0]
    lo = g["accessors"][world["attributes"]["POSITION"]]["min"]
    hi = g["accessors"][world["attributes"]["POSITION"]]["max"]
    # UE x -32..2592, y -1568..1056, z -32..544 -> glTF (x, z, y) / 100.
    assert lo == pytest.approx([-0.32, -0.32, -15.68]) and hi == pytest.approx([25.92, 5.44, 10.56])


def test_dangling_actor_ref_is_reported(testbox_dir):
    g = copy.deepcopy(_load(testbox_dir, "sim/gameplay.json"))
    g["actors"][0]["props"]["Owner"] = {"$actor": "Nobody"}
    assert any("unknown actor Nobody" in e for e in gameplay_errors(g))


def test_stale_tag_index_is_reported(testbox_dir):
    g = copy.deepcopy(_load(testbox_dir, "sim/gameplay.json"))
    g["actors"][0]["props"]["Tag"] = "Renamed"
    assert any("tagIndex" in e for e in gameplay_errors(g))


def test_schema_rejects_bad_ge(testbox_dir):
    t = copy.deepcopy(_load(testbox_dir, "sim/traversal.json"))
    t["ges"][0]["kind"] = "PIPE"   # needs 3 points
    assert schema_errors("traversal", t)
    t["ges"][0]["kind"] = "ROPE"
    assert schema_errors("traversal", t)


def test_schema_rejects_bad_props(testbox_dir):
    g = copy.deepcopy(_load(testbox_dir, "sim/gameplay.json"))
    g["actors"][0]["props"]["Bad"] = {"$actor": 5}
    assert schema_errors("gameplay", g)
    g = copy.deepcopy(_load(testbox_dir, "sim/gameplay.json"))
    del g["levelInfo"]["props"]["GEActivationMask"]
    assert schema_errors("gameplay", g)


def test_tampered_file_fails_hash_check(testbox_dir, tmp_path):
    root = tmp_path / "tb"
    testbox.generate(root)
    t = json.loads((root / "sim" / "traversal.json").read_text(encoding="utf-8"))
    t["ges"][0]["mask"] = 1
    (root / "sim" / "traversal.json").write_bytes(canonical.dump_file(t))
    errs = validate_svmap(root)
    assert any("manifest" in e for e in errs)


def test_traversal_point_count_check():
    t = {"ges": [{"id": "GE0", "kind": "LADDER", "points": [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0]], "mask": 0,
                  "platform": 0, "src": {"kind": "synthetic"}}]}
    assert traversal_errors(t)
