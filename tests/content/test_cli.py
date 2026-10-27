from __future__ import annotations

import pytest
from svmbuild import cli
from svmbuild.paths import default_game_root


def _tree(root):
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}


def test_testbox_step_matches_fixture(tmp_path, testbox_dir, capsys):
    out = tmp_path / "content"
    args = ["testbox", "--out", str(out), "--game", str(tmp_path / "nogame")]
    assert cli.main(args) == 0
    assert "testbox       -            built" in capsys.readouterr().out
    assert _tree(out / "maps" / "TestBox") == _tree(testbox_dir)
    assert cli.main(args) == 0
    assert "cached" in capsys.readouterr().out


def test_list_and_unknown_step(capsys):
    assert cli.main(["--list"]) == 0
    listing = capsys.readouterr().out
    for name in ("classes", "textures", "meshes", "maps", "chars", "audio", "ui", "validate", "maps-preview",
                 "testbox"):
        assert name in listing
    assert cli.main(["nonsense"]) == 2


def test_out_inside_game_is_refused(tmp_path):
    game = tmp_path / "game"
    (game / "Packages" / "Maps").mkdir(parents=True)
    assert cli.main(["testbox", "--game", str(game), "--out", str(game / "out")]) == 2


GAME = default_game_root()


@pytest.mark.skipif(not (GAME / "Packages" / "Maps" / "SteeE.sdc").exists(), reason="reference install not present")
def test_maps_preview_steee(tmp_path, capsys):
    out = tmp_path / "content"
    args = ["maps-preview", "--maps", "steee", "--out", str(out), "--game", str(GAME)]
    assert cli.main(args) == 0
    first = capsys.readouterr().out
    assert "maps-preview  SteeE        built" in first
    preview = out / "preview" / "SteeE"
    assert (preview / "SteeE.gltf").exists() and (preview / "scene.json").exists()
    assert cli.main(args) == 0
    assert "cached" in capsys.readouterr().out
