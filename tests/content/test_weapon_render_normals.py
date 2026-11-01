"""First-person geometry must retain the normals cooked into the game's render mesh."""

import json
from pathlib import Path

import numpy as np
import pytest

from svmbuild.paths import default_game_root, import_scctpkg


GAME = default_game_root()
pytestmark = pytest.mark.skipif(
    not (GAME / "Packages/Animations/SWeaponFirst.ukx").exists(), reason="reference install not present")


@pytest.mark.parametrize("name", ["1DEF_01", "1DEF_shotgun", "1DEF_uzi"])
def test_cooked_weapon_normals_survive_export(name: str, tmp_path: Path):
    import_scctpkg()
    from scctpkg.packageset import PackageSet
    from scctpkg.skeletalmesh import read_skeletal_mesh
    from scctpkg.export_skel_gltf import export_skeletal

    packages = PackageSet([GAME / "Packages"])
    package = packages.get("SWeaponFirst")
    export = package.find_export(name, "SkeletalMesh")
    base = read_skeletal_mesh(package, export)
    cooked = read_skeletal_mesh(package, export, prefer_render_lod=True)
    assert cooked.normals is not None
    assert cooked.normals.shape == (len(cooked.wedges), 3)
    assert np.allclose(np.linalg.norm(cooked.normals, axis=1), 1, atol=0.004)

    # Retaining authored normal splits must not move triangles or change their UVs/materials.
    def triangles(mesh):
        return sorted((int(material), tuple(sorted(
            tuple(np.round(np.r_[mesh.points[int(mesh.wedges[v, 0])], mesh.wedges[v, 1:]], 4))
            for v in face))) for face, material in zip(mesh.faces, mesh.face_material))
    assert triangles(base) == triangles(cooked)

    path = export_skeletal(package, cooked, packages, tmp_path, tmp_path / "textures")
    doc = json.loads(path.read_text())
    accessor = doc["accessors"][doc["meshes"][0]["primitives"][0]["attributes"]["NORMAL"]]
    view = doc["bufferViews"][accessor["bufferView"]]
    data = path.with_suffix(".bin").read_bytes()
    normals = np.frombuffer(data, dtype="<f4", count=accessor["count"] * 3,
                            offset=view.get("byteOffset", 0) + accessor.get("byteOffset", 0)).reshape(-1, 3)
    expected = cooked.normals / np.linalg.norm(cooked.normals, axis=1, keepdims=True)
    np.testing.assert_allclose(normals, expected[:, [0, 2, 1]], atol=1e-6)
