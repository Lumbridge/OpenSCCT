"""The chars step's skin swaps: materials a class puts into a mesh's Skins at run time (content/svmbuild/charconv.py)."""

from __future__ import annotations

from pathlib import Path

import pytest

from svmbuild.charconv import SKIN_SWAPS, export_skin_swaps
from svmbuild.paths import default_game_root
from svmbuild.sources.env import import_formats

GAME = default_game_root()
HAVE_GAME = (GAME / "Packages" / "Textures" / "SWeaponTex.utx").exists()

ROF = ("SWeaponTex.SI_rof_single", "SWeaponTex.SI_rof_burst", "SWeaponTex.SI_rof_auto")


def test_the_merc_rifle_rof_screens_are_swapped_with_the_first_person_weapons():
    # SWeaponDefense.SetMode swaps Skins[3] between the three screens (spec/equipment/merc-rifle.md rule 16).
    assert SKIN_SWAPS["SWeaponFirst"] == ROF


@pytest.mark.skipif(not HAVE_GAME, reason="reference install not present")
def test_rof_screens_export_as_the_mesh_materials_name_them(tmp_path: Path):
    import_formats()
    from scctpkg.packageset import PackageSet  # noqa: PLC0415

    written = export_skin_swaps(PackageSet([GAME / "Packages"]), ROF, tmp_path)
    assert written == list(ROF)
    # The rifle mesh names its slot 3 material "SWeaponTex.SI_rof_single"; the view finds the swaps by that pattern.
    for key in ROF:
        png = tmp_path / (key.replace(".", "_") + ".png")
        assert png.is_file() and png.stat().st_size > 0, png.name
