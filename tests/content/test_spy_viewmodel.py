"""First-person derivatives retain stock skin and mechanism data."""

import numpy as np

from svmbuild.sidearm_models import append, values, world_matrices
from svmbuild.spy_viewmodel import LASER_OFFSET, bind_sleeves, install_models, refined_gloves
from svmbuild.writers.glb import pack_glb, read_glb


def stock_mesh(path, names, clips):
    doc = {
        "asset": {"version": "2.0"},
        "accessors": [],
        "bufferViews": [],
        "images": [{"uri": "textures/stock.png"}],
        "materials": [{"name": "Stock.Material"}],
        "nodes": [{"name": names[0], "children": list(range(1, len(names)))},
                  *({"name": name} for name in names[1:]),
                  {"name": "Stock", "mesh": 0, "skin": 0}],
        "scenes": [{"nodes": [0, len(names)]}],
        "scene": 0,
        "extras": {"svmap": {"mesh": "Package.Stock", "animation": "Stock.anim.json"}},
    }
    data = bytearray()
    bind = append(doc, data, np.tile(np.eye(4).reshape(1, 16), (len(names), 1)), "MAT4")
    doc["skins"] = [{"joints": list(range(len(names))), "inverseBindMatrices": bind}]
    positions = np.arange(27, dtype=np.float32).reshape(9, 3)
    joint = [1, 1, 1, 2, 2, 2, 0, 0, 0]
    attributes = {
        "POSITION": append(doc, data, positions, "VEC3"),
        "NORMAL": append(doc, data, np.tile([0, 0, 1], (9, 1)), "VEC3"),
        "TEXCOORD_0": append(doc, data, positions[:, :2] / 30, "VEC2"),
        "JOINTS_0": append(doc, data, np.column_stack((joint, np.zeros((9, 3)))), "VEC4", 5123),
        "WEIGHTS_0": append(doc, data, np.tile([1, 0, 0, 0], (9, 1)), "VEC4"),
    }
    triangles = append(doc, data, range(9), "SCALAR", 5125)
    doc["meshes"] = [{"primitives": [{"attributes": attributes, "indices": triangles, "material": 0}]}]
    doc["animations"] = []
    for number, name in enumerate(clips, 1):
        times = append(doc, data, [0, number], "SCALAR")
        poses = append(doc, data, [[0, 0, 0], [number, 0, 0]], "VEC3")
        doc["animations"].append({
            "name": name,
            "channels": [{"sampler": 0, "target": {"node": 0, "path": "translation"}}],
            "samplers": [{"input": times, "output": poses}],
        })
    path.parent.mkdir(parents=True)
    path.write_bytes(pack_glb(doc, data))
    return path.read_bytes()


def test_derivatives_are_stable_and_leave_world_assets_untouched(tmp_path):
    arms = tmp_path / "SPerso/ATT_01.glb"
    weapon = tmp_path / "SWeaponFirst/p190.glb"
    arm_source = stock_mesh(arms, ["B", "B L Hand", "B R Hand", "B L Clavicle", "B R Clavicle"],
                            ["WaitStSpFd2", "WorldOnly"])
    weapon_source = stock_mesh(weapon, ["corps", "FX", "canons"], ["DrawP190", "WaitP190"])

    outputs = ["SPerso/ATT_01_fp.glb", "SWeaponFirst/p190_fp.glb"]
    assert install_models(tmp_path) == outputs
    first_arms, first_weapon = [(tmp_path / path).read_bytes() for path in outputs]
    assert install_models(tmp_path) == outputs
    assert [(tmp_path / path).read_bytes() for path in outputs] == [first_arms, first_weapon]
    assert arms.read_bytes() == arm_source
    assert weapon.read_bytes() == weapon_source

    arm_doc, arm_data = read_glb(first_arms)
    source_doc, source_data = read_glb(arm_source)
    prim = arm_doc["meshes"][0]["primitives"][0]
    source_prim = source_doc["meshes"][0]["primitives"][0]
    assert arm_doc["images"] == source_doc["images"]
    assert arm_doc["materials"] == source_doc["materials"]
    assert arm_doc["nodes"] == source_doc["nodes"]
    assert [clip["name"] for clip in arm_doc["animations"]] == ["WaitStSpFd2"]
    assert values(arm_doc, arm_data, prim["indices"]).size == 24
    for name, accessor in prim["attributes"].items():
        stock = values(source_doc, source_data, source_prim["attributes"][name])
        np.testing.assert_array_equal(values(arm_doc, arm_data, accessor)[:6], stock[:6])

    weapon_doc, weapon_data = read_glb(first_weapon)
    source_doc, source_data = read_glb(weapon_source)
    assert weapon_doc["materials"] == source_doc["materials"]
    assert weapon_doc["nodes"][1]["children"] == [4]
    assert [clip["name"] for clip in weapon_doc["animations"]] == ["DrawP190", "WaitP190"]
    for old, new in zip(source_doc["animations"], weapon_doc["animations"]):
        for name in ("input", "output"):
            np.testing.assert_array_equal(values(source_doc, source_data, old["samplers"][0][name]),
                                          values(weapon_doc, weapon_data, new["samplers"][0][name]))
    world, _ = world_matrices(weapon_doc)
    assert weapon_doc["skins"][0]["joints"] == [0, 1, 2, 4]
    np.testing.assert_allclose(world[4][:3, 3] - world[1][:3, 3], LASER_OFFSET)
    bindings = values(weapon_doc, weapon_data, weapon_doc["skins"][0]["inverseBindMatrices"])
    np.testing.assert_allclose(world[4] @ bindings[-1].reshape(4, 4).T, np.eye(4), atol=1e-8)


def test_missing_stock_assets_need_no_output(tmp_path):
    assert install_models(tmp_path) == []


def test_sleeve_cuts_keep_their_weights_on_the_same_side():
    attributes = {
        "JOINTS_0": np.array([[1, 0, 3, 0], [2, 0, 0, 0], [1, 3, 0, 0]]),
        "WEIGHTS_0": np.array([[.60, .30, .10, 0], [.65, .35, 0, 0], [.80, .20, 0, 0]]),
    }
    bind_sleeves(attributes, ([1], [2]), [3, 4])
    np.testing.assert_array_equal(attributes["JOINTS_0"], [[1, 0, 3, 0], [2, 4, 0, 0], [1, 3, 0, 0]])
    np.testing.assert_allclose(attributes["WEIGHTS_0"], [[.60, 0, .40, 0], [.65, .35, 0, 0], [.80, .20, 0, 0]])
    np.testing.assert_allclose(attributes["WEIGHTS_0"].sum(axis=1), 1)


def test_rounding_shares_physical_midpoint_across_uv_seam_and_pins_boundaries():
    attributes = {
        "POSITION": np.array([[-.01, 0, 0], [.01, 0, 0], [0, .01, 0],
                              [-.01, 0, 0], [.01, 0, 0], [0, -.01, 0]]),
        "NORMAL": np.array([[-.6, 0, .8], [.6, 0, .8], [0, .6, .8],
                            [-.6, 0, .8], [.6, 0, .8], [0, -.6, .8]]),
        "TEXCOORD_0": np.array([[0, 0], [.2, 0], [0, .2], [.4, 0], [.6, 0], [.6, .2]]),
        "JOINTS_0": np.array([[1, 2, 3, 4], [4, 5, 6, 7], [1, 0, 0, 0]] * 2),
        "WEIGHTS_0": np.array([[.45, .30, .15, .10], [.40, .30, .20, .10], [1, 0, 0, 0]] * 2),
    }
    triangles = np.array([[0, 1, 2], [4, 3, 5]])
    refined, indices = refined_gloves(attributes, triangles, range(1, 8))
    assert indices.shape == (8, 3)
    for name, values_ in attributes.items():
        np.testing.assert_array_equal(refined[name][:6], values_)

    seam = [np.flatnonzero(np.isclose(refined["TEXCOORD_0"][:, 0], x))[0] for x in (.1, .5)]
    np.testing.assert_allclose(refined["POSITION"][seam], [[0, 0, .002], [0, 0, .002]])
    np.testing.assert_array_equal(refined["JOINTS_0"][seam], [[4, 1, 2, 5], [4, 1, 2, 5]])
    np.testing.assert_allclose(refined["WEIGHTS_0"][seam].sum(axis=1), [1, 1])
    assert np.count_nonzero(refined["POSITION"][:, 2]) == 2

    sleeve, _ = refined_gloves(attributes, triangles, [])
    assert np.count_nonzero(sleeve["POSITION"][:, 2]) == 0
