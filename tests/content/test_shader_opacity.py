"""Shader opacity uses alpha even when constant; RGB must never become the coverage mask."""
import builtins
import io
from pathlib import Path

import pytest
from PIL import Image
from svmbuild.sources.env import import_formats


@pytest.fixture(autouse=True)
def synthetic_actor_defaults(monkeypatch):
    # export_gltf imports level metadata even for pure image operations. These tests
    # never extract actors, so no original-game class defaults are needed on CI.
    original_open = builtins.open

    def open_for_image_helpers(file, *args, **kwargs):
        if isinstance(file, (str, Path)):
            path = Path(file)
            if path.name == "class_defaults.json" and path.parent.name == "scctpkg" and not path.exists():
                return io.StringIO("{}")
        return original_open(file, *args, **kwargs)

    monkeypatch.setattr(builtins, "open", open_for_image_helpers)


def test_constant_shader_opacity_is_not_replaced_with_luminance():
    import_formats()
    from scctpkg.export_gltf import _with_opacity
    diffuse = Image.new('RGBA', (2, 2), (20, 80, 120, 255))
    opacity = Image.new('RGBA', (2, 2), (0, 0, 0, 144))
    opacity.putpixel((1, 1), (255, 255, 255, 144))
    result = _with_opacity(diffuse, opacity)
    assert result.getchannel('A').getextrema() == (144, 144)
    assert result.getpixel((0, 0)) == (20, 80, 120, 144)


def test_opaque_shader_opacity_stays_opaque_even_when_rgb_is_black():
    import_formats()
    from scctpkg.export_gltf import _with_opacity
    result = _with_opacity(Image.new('RGBA', (4, 4), (20, 80, 120, 40)),
                           Image.new('RGB', (2, 2), (0, 0, 0)))
    assert result.getchannel('A').getextrema() == (255, 255)


def test_separately_animated_opacity_keeps_alpha_channel(tmp_path):
    import_formats()
    from scctpkg.export_gltf import _layer_flags
    from scctpkg.materials import ResolvedMaterial
    from scctpkg.texture import Texture
    opacity = Texture('mask', 2, 2, 8, Image.new('RGBA', (2, 2), (0, 0, 0, 144)), {}, True)
    material = ResolvedMaterial(key='glass', texture=None, opacity=opacity, opacity_key='mask', opacity_layer=True)
    flags = _layer_flags('glass', material, tmp_path, {})
    assert flags['opacity_lum'] is False

