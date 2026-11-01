"""The rifle's normal-vision highlights belong to the shader behind its heat/EMF modifiers."""

from pathlib import Path

import pytest

from svmbuild.charconv import export_material_layers
from svmbuild.paths import default_game_root
from svmbuild.sources.env import import_formats


GAME = default_game_root()


@pytest.mark.skipif(not (GAME / "Packages/Textures/SFX_xbox.utx").exists(), reason="reference install not present")
def test_rifle_layers_survive_normal_vision_texture_renaming(tmp_path: Path):
    import_formats()
    from scctpkg.packageset import PackageSet
    from scctpkg.skeletalmesh import read_skeletal_mesh

    pset = PackageSet([GAME / "Packages"])
    pkg = pset.get("SWeaponFirst")
    mesh = read_skeletal_mesh(pkg, pkg.find_export("1DEF_01", "SkeletalMesh"))
    names = ["SWeaponTex.1DEF_01", "SWeaponTex.M160", "SWeaponTex.viseur_snipe",
             "SWeaponTex.SI_rof_single", "SWeaponTex.heat_first_att02"]
    doc = {"materials": [{"name": name} for name in names]}
    export_material_layers(pkg, pset, mesh, doc, tmp_path / "textures")

    assert [m["name"] for m in doc["materials"]] == names
    gun = doc["materials"][1]["extras"]
    assert gun["scct_vision_source"] == "SWeaponTex.Armes.heat_m160"
    assert gun["scct_env_texture"] == "textures/SFX_xbox_envmap_gundef.png"
    assert gun["scct_spec_mask"] == "textures/SFX_xbox_M160_specular_mask.png"
    assert gun["scct_env_world"] is False
    assert all(0 < c < 1 for c in gun["scct_env_color"])
    screen = doc["materials"][3]["extras"]
    assert screen["scct_self_illum"] == screen["scct_self_illum_mask"] == "textures/SWeaponTex_ROF_Single.png"
    for material in doc["materials"]:
        for key, value in material.get("extras", {}).items():
            if isinstance(value, str) and value.startswith("textures/"):
                assert (tmp_path / value).is_file(), key
