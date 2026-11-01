"""Visible light sprites export their own materials without becoming coronas or depending on mesh usage."""
import builtins
import io
from pathlib import Path
from types import SimpleNamespace

import pytest
from PIL import Image
from svmbuild.mapconv.render import RenderBuilder, _sprite_blend
from svmbuild.mapconv.source import ActorInfo
from svmbuild.sources.typedprops import Notes, Ref, default_resolver


def test_actor_style_overrides_only_normal_material_blending():
    material = SimpleNamespace(invisible=False, additive=False, darken=False, modulate="", translucent=True, masked=False)
    assert _sprite_blend(material, 1) == "translucent"
    assert _sprite_blend(material, 6) == "additive"
    assert _sprite_blend(material, 4) == "modulate"
    assert _sprite_blend(material, 2) == "masked"


def test_visible_finalblend_sprite_exports_texture_size_scale_and_material_flags(tmp_path, monkeypatch):
    from svmbuild.paths import import_scctpkg

    # The PNG helper imports map metadata; this synthetic test needs no reference-install class-default dump.
    original_open = builtins.open

    def open_for_image_helpers(file, *args, **kwargs):
        if isinstance(file, (str, Path)):
            path = Path(file)
            if path.name == "class_defaults.json" and path.parent.name == "scctpkg" and not path.exists():
                return io.StringIO("{}")
        return original_open(file, *args, **kwargs)

    monkeypatch.setattr(builtins, "open", open_for_image_helpers)
    import_scctpkg()
    from scctpkg import materials
    from scctpkg.texture import Texture

    ref = Ref(1, "FX.GlowBlend", False)
    visible = ActorInfo(1, 1, "Visible", "Engine.Light",
                        {"bHidden": False, "Texture": ref, "DrawScale": .4}, "render")
    hidden = ActorInfo(2, 2, "Hidden", "Engine.Light", {"Texture": ref}, "render")
    corona = ActorInfo(3, 3, "Corona", "Engine.Light", {"bHidden": False, "Texture": ref, "bCorona": True}, "render")
    mesh = ActorInfo(4, 4, "Mesh", "Engine.Light", {"bHidden": False, "Texture": ref, "DrawType": 8}, "render")
    texture = Texture("glow", 32, 16, 8, Image.new("RGBA", (32, 16), (30, 20, 10, 128)), {}, True)
    result = materials.ResolvedMaterial("FX.GlowBlend", texture, translucent=True, masked=True, two_sided=True)
    resolver = SimpleNamespace(resolve=lambda *_: result, _props=lambda *_: {
        "FrameBufferBlending": 2, "AlphaTest": True, "AlphaRef": 42, "ZWrite": False, "ZTest": True,
    })
    monkeypatch.setattr(materials, "MaterialResolver", lambda _: resolver)
    defaults = {"bHidden": True}
    value = lambda actor, key, fallback=None: actor.props.get(key, defaults.get(key, fallback))
    src = SimpleNamespace(actors=[visible, hidden, corona, mesh], pset=object(),
                          db=SimpleNamespace(is_a=lambda *_: True), value=value,
                          flag=lambda actor, key: bool(value(actor, key, False)),
                          ref_export=lambda _: (object(), SimpleNamespace(index=0, class_name="FinalBlend")),
                          notes=Notes(), resolve=default_resolver)
    builder = RenderBuilder(src, tmp_path / "work", set())
    sprites = builder.build_light_sprites(tmp_path / "textures")
    assert set(sprites) == {"Visible"}
    sprite = sprites["Visible"]
    assert (sprite["width"], sprite["height"], sprite["draw_scale"]) == (32, 16, .4)
    assert sprite["draw_scale3d"] == [1, 1, 1]
    assert sprite["blend"] == "translucent" and sprite["two_sided"]
    assert sprite["alpha_ref"] == 42 and sprite["alpha_test"]
    assert not sprite["z_write"] and sprite["z_test"]
    assert Image.open(tmp_path / sprite["texture_uri"]).getpixel((0, 0)) == (30, 20, 10, 128)
    lighting = builder.build_lighting({"actors": [{"name": "Visible", "kind": "light"}]}, {}, [], sprites)
    assert lighting["lights"][0]["sprite"] == sprite


@pytest.mark.parametrize("map_name,count,source,blend,size", [
    ("SteeE", 8, "sfx.Flare.def_flare", "additive", 128),
    ("ClarD", 545, "claritysoft.FX.flstreet", "translucent", 64),
])
def test_reference_light_sprites_export_without_a_mesh_using_the_texture(tmp_path, map_name, count, source, blend, size):
    from svmbuild.paths import default_game_root, import_scctpkg
    from svmbuild.sources.classdb import class_db
    from svmbuild.mapconv.source import MapSource

    game = default_game_root()
    if not (game / "Packages" / "Maps" / f"{map_name}.sdc").is_file():
        pytest.skip("reference install not present")
    import_scctpkg(require_class_defaults=True)
    builder = RenderBuilder(MapSource(game, map_name, class_db(str(game))), tmp_path / "work", set())
    sprites = builder.build_light_sprites(tmp_path / "textures")
    assert len(sprites) == count
    sprite = next(s for s in sprites.values() if s["source"] == source)
    assert (sprite["blend"], sprite["width"], sprite["height"]) == (blend, size, size)
    assert (tmp_path / sprite["texture_uri"]).is_file()
    assert builder.stats["lightSprites"] == count
