"""Render overrides retain material bindings, transformed UVs and baked light."""

import hashlib
import json

import numpy as np
import pytest

from svmbuild import remastered_meshes as overrides


def fixture(tmp_path, monkeypatch):
    monkeypatch.setattr(overrides, "REPO_ROOT", tmp_path)
    pos = np.asarray([[0, 0, 0], [1, 0, 0], [0, 1, 0]], "<f4")
    uv = pos[:, :2]
    new_pos = np.concatenate([pos, [[0.25, 0.25, 0]]]).astype("<f4")
    file = tmp_path / "mesh.npz"
    np.savez(file, positions=new_pos, normals=np.tile([0, 0, 1], (4, 1)).astype("<f4"), uv=new_pos[:, :2],
             template_uv=uv, indices_0=np.array([0, 1, 3, 1, 2, 3, 2, 0, 3], "<u4"))
    manifest = {"format": "svm.mesh-overrides/1", "meshes": {"Pkg.Prop": {"file": file.name, "sha256": hashlib.sha256(file.read_bytes()).hexdigest()}}}
    path = tmp_path / "enabled.json"
    path.write_text(json.dumps(manifest))
    monkeypatch.setenv("SVM_MESH_OVERRIDES", str(path))
    doc = {"nodes": [{"mesh": 0, "extras": {"svmap": {"asset": "Pkg.Prop"}}}],
           "meshes": [{"primitives": [{"material": 17, "indices": 3, "extras": {"scctSection": 0}, "attributes": {"POSITION": 0, "TEXCOORD_0": 1, "COLOR_0": 2}}]}],
           "bufferViews": [], "accessors": []}
    binary = bytearray()
    for arr, kind, component in [(pos, "VEC3", 5126), (uv * 2 + 0.5, "VEC2", 5126), (np.tile([60, 80, 100, 255], (3, 1)).astype("u1"), "VEC4", 5121), (np.array([0, 1, 2], "<u4"), "SCALAR", 5125)]:
        doc["bufferViews"].append({"byteOffset": len(binary), "byteLength": arr.nbytes})
        doc["accessors"].append({"bufferView": len(doc["bufferViews"]) - 1, "count": len(arr), "type": kind, "componentType": component, "normalized": component == 5121})
        binary.extend(arr.tobytes())
    return doc, bytes(binary), path, file


def test_override_preserves_material_uv_transform_and_constant_baked_light(tmp_path, monkeypatch):
    doc, binary, _, _ = fixture(tmp_path, monkeypatch)
    result, report = overrides.apply(doc, binary)
    primitive = doc["meshes"][0]["primitives"][0]
    assert primitive["material"] == 17 and report["assets"] == ["Pkg.Prop"]
    attrs = primitive["attributes"]
    np.testing.assert_allclose(overrides._array(doc, result, attrs["TEXCOORD_0"])[3], [1, 1])
    np.testing.assert_array_equal(overrides._array(doc, result, attrs["COLOR_0"]), np.tile([60, 80, 100, 255], (4, 1)))
    assert result[:len(binary)] == binary


def test_changed_override_bytes_are_rejected(tmp_path, monkeypatch):
    doc, binary, _, file = fixture(tmp_path, monkeypatch)
    file.write_bytes(b"different")
    with pytest.raises(ValueError, match="hash mismatch"):
        overrides.apply(doc, binary)


def test_lightmapped_mesh_requires_rebake(tmp_path, monkeypatch):
    doc, binary, _, _ = fixture(tmp_path, monkeypatch)
    doc["meshes"][0]["primitives"][0]["attributes"]["TEXCOORD_1"] = 1
    result, report = overrides.apply(doc, binary)
    assert result == binary and report["meshes"] == 0
    assert "rebake" in report["skipped"][0]["reason"]


def test_light_transfer_uses_the_face_instead_of_a_nearby_dark_vertex():
    old = np.array([[0, 0, 0], [10, 0, 0], [0, 10, 0], [2, 2, -0.01]], np.float32)
    colours = np.array([[90, 60, 30, 255]] * 3 + [[0, 0, 0, 255]], np.uint8)
    result = overrides._colours(np.array([[2, 2, 0]], np.float32), old, colours, np.array([0, 1, 2], np.uint32))
    np.testing.assert_array_equal(result, [[90, 60, 30, 255]])
