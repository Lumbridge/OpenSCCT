"""Unlit direction markers still need their Shader's emissive symbols and panning layers."""
from pathlib import Path

import pytest
from svmbuild.paths import default_game_root
from svmbuild.sources.env import import_formats

GAME = default_game_root()


@pytest.mark.skipif(not (GAME / "Packages").exists(), reason="reference install not present")
def test_unlit_help_markers_preserve_emissive_layers(tmp_path: Path):
    import_formats()
    from scctpkg.packageset import PackageSet
    from scctpkg.staticmesh import read_static_mesh
    from scctpkg.materials import MaterialResolver
    from scctpkg.export_gltf import GltfWriter, _static_mesh_prims

    pset = PackageSet([GAME / "Packages"])
    pkg = pset.get("SQU13_STM")
    resolver = MaterialResolver(pset)
    for name in ["help_path_INFO", "help_path_SQA"]:
        mesh = read_static_mesh(pkg, pkg.find_export(name, "StaticMesh"))
        writer = GltfWriter()
        textures = {}
        _static_mesh_prims(mesh, pkg, resolver, writer, tmp_path, textures)
        _static_mesh_prims(mesh, pkg, resolver, writer, tmp_path, textures, unlit=True)
        variants = [(key, flags) for key, flags in writer.material_flags.items() if key.endswith("__unlit")]
        assert variants
        for key, flags in variants:
            original = writer.material_flags[key[:-7]]
            assert flags["unlit"]
            assert flags["self_illum"] == original["self_illum"]
            assert flags["self_illum_mask"] == original["self_illum_mask"]
            for layer in ["self_illum_layer", "self_illum_mask_layer"]:
                assert flags.get(layer) == original.get(layer)
