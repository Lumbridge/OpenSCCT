"""MaterialSequence exports preserve every timed input, its UV animation and cache identity."""
from pathlib import Path
from types import SimpleNamespace

from PIL import Image
import pytest

from svmbuild.sources.env import import_formats


def test_timed_inputs_and_nested_glow_are_exported(tmp_path: Path):
    import_formats()
    from scctpkg.materials import MaterialResolver
    from scctpkg.texture import Texture
    from scctpkg.export_gltf import _layer_flags

    pkg = SimpleNamespace(path=Path("Synthetic.utx"))
    exports = {i: SimpleNamespace(index=i, name=f"M{i}", class_name=cls) for i, cls in
               [(1, "Texture"), (2, "Texture"), (3, "TexPanner"), (4, "MaterialSequence"), (5, "Shader")]}
    props = {3: {"Material": 2, "PanRate": 0.25, "PanDirection": {"Pitch": 0, "Yaw": 0, "Roll": 0}},
             4: {"SequenceItems": [{"Material": 1, "Time": 2.5, "Action": 0},
                                   {"Material": 3, "Time": 0.4, "Action": 1}]},
             5: {"SelfIllumination": 4}}
    pset = SimpleNamespace(import_path=lambda p, r: f"Synthetic.M{r}", resolve=lambda p, r: (pkg, exports[r]))
    resolver = MaterialResolver(pset)
    resolver._props = lambda p, e: props.get(e.index, {})
    resolver.texture_for = lambda p, e: Texture(e.name, 4, 4, 5, Image.new("RGBA", (4, 4), (e.index * 50, 0, 0, 255)), {})
    result = resolver.resolve(pkg, 4)
    assert resolver.resolve(pkg, 4) is result
    assert len(result.sequences["diffuse"]["items"]) == 2
    texture_dir = tmp_path / "textures"
    texture_dir.mkdir()
    flags = _layer_flags(result.key, result, texture_dir, {})
    sequence = flags["sequences"]["diffuse"]
    assert sequence["loop"] is True
    assert [(item["time"], item["action"]) for item in sequence["items"]] == [(2.5, 0), (0.4, 1)]
    assert sequence["items"][1]["anim"]["type"] == "panner"
    assert all((tmp_path / item["texture"]).exists() for item in sequence["items"])
    glow = resolver.resolve(pkg, 5)
    assert glow.sequences["self_illum"] == glow.sequences["diffuse"]


@pytest.mark.parametrize("wrapper", [None, "TexPanner", "FinalBlend"])
def test_show_frames_keep_complete_blend_illumination_and_opacity_state(tmp_path: Path, wrapper):
    import_formats()
    from scctpkg.materials import MaterialResolver
    from scctpkg.texture import Texture
    from scctpkg.export_gltf import _layer_flags

    pkg = SimpleNamespace(path=Path("Synthetic.utx"))
    exports = {i: SimpleNamespace(index=i, name=f"M{i}", class_name=cls) for i, cls in
               [(1, "Texture"), (2, "Texture"), (3, "Shader"), (4, "Shader"), (5, "MaterialSequence"),
                (6, wrapper or "Shader"), (7, "Shader")]}
    props = {3: {"Diffuse": 1, "Opacity": 1, "SelfIllumination": 1, "OutputBlending": 3, "TwoSided": True},
             4: {"Diffuse": 2, "OutputBlending": 3},
             5: {"SequenceItems": [{"Material": 3, "Time": .5, "Action": 0},
                                   {"Material": 4, "Time": 1., "Action": 0}]},
             6: {"Material": 5, "PanRate": .25, "PanDirection": {"Yaw": 0}, "FrameBufferBlending": 2},
             7: {"SelfIllumination": 5}}
    pset = SimpleNamespace(import_path=lambda p, r: f"Synthetic.M{r}", resolve=lambda p, r: (pkg, exports[r]))
    resolver = MaterialResolver(pset)
    resolver._props = lambda p, e: props.get(e.index, {})
    resolver.texture_for = lambda p, e: Texture(e.name, 4, 4, 5, Image.new("RGBA", (4, 4), (120, 50, 0, 64)), {})
    result = resolver.resolve(pkg, 6 if wrapper else 5)
    tex_dir = tmp_path / "textures"
    tex_dir.mkdir()
    flags = _layer_flags(result.key, result, tex_dir, {})
    sequence = flags["sequences"]["diffuse"]
    assert sequence["switch_material"]
    first, second = [item["material"] for item in sequence["items"]]
    assert first["blend"] == "translucent"
    assert second["blend"] == ("translucent" if wrapper == "FinalBlend" else "additive")
    assert first["unlit"] is True and second["unlit"] is False
    assert first["two_sided"] is True and second["two_sided"] is False
    assert "self_illum" in first and "self_illum" not in second
    assert "__op.png" in first["texture"]
    assert Image.open(tmp_path / first["texture"]).getpixel((0, 0))[3] == 64
    if wrapper == "TexPanner":
        assert first["uv_anim"]["type"] == second["uv_anim"]["type"] == "panner"
    # As a containing Shader's colour input, a sequence must not replace that Shader's own blend state.
    glow = resolver.resolve(pkg, 7)
    assert glow.sequences["diffuse"]["switch_material"] is False
    assert all("material" not in item for item in glow.sequences["diffuse"]["items"])


def test_incompatible_fade_is_diagnosed_instead_of_freezing_first_state():
    import_formats()
    from scctpkg.materials import MaterialResolver
    from scctpkg.texture import Texture
    pkg = SimpleNamespace(path=Path("Synthetic.utx"))
    exports = {i: SimpleNamespace(index=i, name=f"M{i}", class_name=cls) for i, cls in
               [(1, "Texture"), (2, "Shader"), (3, "MaterialSequence")]}
    props = {2: {"Diffuse": 1, "OutputBlending": 3},
             3: {"SequenceItems": [{"Material": 1, "Time": 1., "Action": 0}, {"Material": 2, "Time": 1., "Action": 1}]}}
    pset = SimpleNamespace(import_path=lambda p, r: f"Synthetic.M{r}", resolve=lambda p, r: (pkg, exports[r]))
    resolver = MaterialResolver(pset)
    resolver._props = lambda p, e: props.get(e.index, {})
    resolver.texture_for = lambda p, e: Texture(e.name, 4, 4, 5, Image.new("RGBA", (4, 4), (100, 0, 0, 255)), {})
    for _ in range(2):
        with pytest.raises(ValueError, match="fades between incompatible material states"):
            resolver.resolve(pkg, 3)
