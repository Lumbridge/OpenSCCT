"""The fx step: effect classes, their sub-emitters and assets (content/svmbuild/fx)."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from svmbuild.fx import effects
from svmbuild.fx.step import FxStep
from svmbuild.paths import default_game_root
from svmbuild.steps import all_steps

GAME = default_game_root()
HAVE_GAME = (GAME / "System" / "_PC_" / "SBase.u").exists()


def test_assets_of_finds_paths_in_values_and_static_arrays():
    assert effects._assets_of({"$asset": "sfx.Emitter.smoke_grenade"}) == ["sfx.Emitter.smoke_grenade"]
    assert effects._assets_of([{"$asset": "a.b"}, None, {"$asset": "c.d"}]) == ["a.b", "c.d"]
    assert effects._assets_of({"$elements": {"0": {"$asset": "x.y"}, "2": {"$asset": "z.w"}}}) == ["x.y", "z.w"]
    assert effects._assets_of(3.0) == []


class _FakeDb:
    """The two ClassDB calls effect_classes makes, over a little class tree."""

    def __init__(self, supers: dict[str, str | None]):
        self.supers = supers
        self.classes = {k: {"super": v} for k, v in supers.items()}

    def find(self, path):
        return path if path in self.supers else None

    def is_a(self, path, base):
        while path is not None:
            if path == base:
                return True
            path = self.supers.get(path)
        return False


def test_effect_classes_takes_the_first_matching_family():
    db = _FakeDb({
        "Engine.Actor": None,
        "Engine.Emitter": "Engine.Actor",
        "Engine.Projectile": "Engine.Actor",
        "SBase.SSmokeEmitter": "Engine.Emitter",
        "SBase.SProjGrenade": "Engine.Projectile",
        "SBase.SProjSmoke": "SBase.SProjGrenade",
        "SBase.SPawn": "Engine.Actor",
    })
    kinds = effects.effect_classes(db)
    assert kinds["SBase.SSmokeEmitter"] == "emitter"
    assert kinds["SBase.SProjSmoke"] == "projectile"
    assert "SBase.SPawn" not in kinds
    assert "Engine.Actor" not in kinds


def test_the_step_is_registered_and_global():
    step = next(s for s in all_steps() if s.name == "fx")
    assert isinstance(step, FxStep)
    assert not step.per_map
    assert step.needs_game


@pytest.mark.skipif(not HAVE_GAME, reason="reference install not present")
def test_reference_install(tmp_path: Path):
    stats = effects.build_pack(GAME, tmp_path)
    assert stats["missing"] == 0, stats
    doc = json.loads((tmp_path / "effects.json").read_text(encoding="utf-8"))
    assert doc["format"] == effects.FORMAT
    classes = doc["classes"]

    # The gadget clouds and the explosion, with their own sub-emitters and textures.
    smoke = classes["SBase.SSmokeEmitter"]
    assert smoke["kind"] == "emitter"
    assert [s["class"] for s in smoke["emitters"]] == ["Engine.SpriteEmitter"]
    cloud = smoke["emitters"][0]
    assert cloud["props"]["MaxParticles"] == 15
    assert cloud["props"]["InitialParticlesPerSecond"] == 200.0
    assert (tmp_path / cloud["texture"]).is_file()
    assert len(classes["SBase.SChaffeEmitter"]["emitters"]) == 3
    assert len(classes["SBase.SGrenadeExplosion"]["emitters"]) == 4
    assert len(classes["SBase.SStickyCamEmitter"]["emitters"]) == 1
    for cls in ("SBase.SSmokeLaunchEmitter", "SBase.SFragLaunchEmitter", "SBase.SSparkBeton", "SBase.SMetalSpark",
                "SBase.SSparkShocker", "SBase.SPoisonEmitter", "SBase.SMinePoisonEmitter", "SBase.SFlashBang"):
        assert classes[cls]["emitters"], cls

    # The casing is a mesh emitter with the shell mesh.
    casing = classes["SBase.SDouilleEmitter"]["emitters"][0]
    assert casing["class"] == "Engine.MeshEmitter"
    assert casing["mesh"] == "SGameplayObjects.Balles.Douille"

    # Thrown and placed gadgets, muzzle flashes and impacts name the meshes and decals they draw.
    meshes = doc["meshes"]
    for cls in ("SBase.SProjSmoke", "SBase.SProjChaffe", "SBase.SProjFrag", "SBase.SProjFlashBang", "SBase.SStickyCam",
                "SBase.SMineProx", "SBase.SMuzzleGenerique", "SBase.SMuzzleSticky", "SBase.sMuzzleCanon"):
        mesh = classes[cls]["assets"]["StaticMesh"]
        assert mesh in meshes, cls
        assert (tmp_path / meshes[mesh]["glb"]).is_file()
    beton = classes["SBase.SImpactEffectBeton"]["assets"]["ImpactMaterial"]
    assert beton == "sfx.Impacts.decal_beton"
    assert (tmp_path / doc["textures"][beton]["png"]).is_file()

    # The Stickess skins of a shocked pawn: the body diffuse with the panned sticky glow over it.
    skins = doc["skins"]
    assert sorted(skins) == sorted(effects.STICKY_SKINS)
    for path, skin in skins.items():
        assert (tmp_path / skin["diffuse"]).is_file(), path
        assert (tmp_path / skin["selfIllum"]).is_file(), path
        assert skin["selfIllumMask"] == skin["selfIllum"], path
        assert skin["pan"] == [0.0, 1.0, 0.5], path
    assert skins["SPersoTextures.alpha_DEF01.Stickess_DEF01_body"]["diffuseObject"] == "SPersoTextures.alpha_DEF01.DEF_01_A_Body"
