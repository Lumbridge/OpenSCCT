"""The ui step end to end: a synthetic install (no texture packages) through the CLI, and the reference install."""

from __future__ import annotations

import json
import struct

import pytest
from svmbuild import cli
from svmbuild.paths import default_game_root

from test_ui_mgm import PACKAGE


def _font(name: str) -> bytes:
    head = b"Magma Font\x00" + struct.pack("<i", 0x82) + name.encode("latin-1") + b"\x00" + b"\xcd" * 3
    entry = struct.pack("<HBBbBB4f", 65, 8, 10, 0, 10, 9, 0.0, 0.5, 0.0625, 0.578125)
    return head + struct.pack("<i", 20) + b"\x00\xe0\x00" + entry


def _game(tmp_path, with_hud=True):
    game = tmp_path / "game"
    (game / "Packages" / "Textures").mkdir(parents=True)
    (game / "Menus" / "Fonts").mkdir(parents=True)
    (game / "System" / "_PC_").mkdir(parents=True)
    if with_hud:
        (game / "Menus" / "Hud.mgm").write_bytes(PACKAGE.replace("<NAME>Synth</NAME>", "<NAME>Hud</NAME>", 1)
                                                 .replace("<PACKAGE>Synth</PACKAGE>", "<PACKAGE>Hud</PACKAGE>")
                                                 .encode("latin-1"))
    (game / "Menus" / "Fonts" / "Test Font 20.mft").write_bytes(_font("Test Font 20"))
    (game / "System" / "_PC_" / "Magma.int").write_bytes(b"[A_Box]\r\nLabel=Piratage\r\n")
    return game


def _tree(root):
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}


def test_synthetic_install_converts_and_reports_what_is_missing(tmp_path, capsys):
    game = _game(tmp_path)
    out = tmp_path / "content"
    args = ["ui", "--game", str(game), "--out", str(out)]
    assert cli.main(args) == 0
    assert "ui            -            built" in capsys.readouterr().out
    ui = out / "shared" / "ui"
    manifest = json.loads((ui / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["format"] == "svm.ui/1"
    assert manifest["packages"]["Hud"] == {"file": "magma/Hud.json", "areas": 3, "pages": ["P_Test"], "materials": 3}
    # No texture packages and no Options/common pages in this install: each gap is listed, nothing else fails.
    assert "Hud_mgm.Atlas" in manifest["missing"] and "Hud_mgm.Dot" in manifest["missing"]
    assert "Menus/Options.mgm" in manifest["missing"] and "SHudTextures.MN_boutons" in manifest["missing"]
    assert "font page Test_Font_20_0" in manifest["missing"]
    assert "System/_PC_/MagmaDynamic.int" in manifest["missing"]
    hud = json.loads((ui / "magma" / "Hud.json").read_text(encoding="utf-8"))
    assert hud["name"] == "Hud" and hud["pages"] == {"P_Test": 0}
    font = json.loads((ui / "fonts" / "Test_Font_20.json").read_text(encoding="utf-8"))
    assert font["format"] == "svm.font/1" and font["size"] == 20 and font["pages"] == [] and font["glyphs"] == []
    table = json.loads((ui / "strings.json").read_text(encoding="utf-8"))
    assert table == {"Magma": {"A_Box": {"Label": "Piratage"}}}
    first = _tree(ui)

    assert cli.main(args) == 0
    assert "cached" in capsys.readouterr().out
    assert cli.main(args + ["--force"]) == 0
    assert _tree(ui) == first   # byte-identical rebuild


def test_missing_hud_package_fails_the_step(tmp_path, capsys):
    game = _game(tmp_path, with_hud=False)
    assert cli.main(["ui", "--game", str(game), "--out", str(tmp_path / "content")]) == 1
    assert "Hud.mgm" in capsys.readouterr().out


GAME = default_game_root()


@pytest.mark.skipif(not (GAME / "Menus" / "Hud.mgm").exists(), reason="reference install not present")
def test_reference_install(tmp_path, capsys):
    out = tmp_path / "content"
    assert cli.main(["ui", "--game", str(GAME), "--out", str(out)]) == 0
    capsys.readouterr()
    ui = out / "shared" / "ui"
    manifest = json.loads((ui / "manifest.json").read_text(encoding="utf-8"))
    assert manifest["missing"] == [] and manifest["warnings"] == []
    assert {"P_HudATT", "P_HudDEF", "P_InterATT", "P_EndGame"} <= set(manifest["packages"]["Hud"]["pages"])
    assert manifest["fonts"]["HandelGothic BT Regular 20"]["pages"] >= 1
    hud = json.loads((ui / "magma" / "Hud.json").read_text(encoding="utf-8"))
    areas = {a["name"]: a for a in hud["areas"]}
    # The page player and the HUD drivers rely on these.
    for name, widget in (("A_Chrono", "Chrono"), ("A_Jauge", "jaugeinterne"), ("A_Gadget", "NomGadget"),
                         ("A_AmmoStock", "TextAmmoStock"), ("A_ChronoHack", "Chrono_Hack"),
                         ("A_MessageSystem", "messageSystem"), ("A_Radar", "IconAlarm1")):
        assert any(w["name"] == widget for w in areas[name]["widgets"]), (name, widget)
    assert all(a["frameRate"] in (5, 10, 20) for a in hud["areas"])
    assert (ui / "textures" / "Hud" / "HUDConfig.png").is_file()
    for tex in ("MN_boutons", "Icones_Gadgets", "Icones_Weapons", "Att_Arrow", "BinocularA_all"):
        assert (ui / "textures" / "SHudTextures" / f"{tex}.png").is_file(), tex
    # The spectator page, the merc effects and the alert box the HUD drives.
    for name, widget in (("A_Spectator", "textNbRemainingTime"), ("A_Flash", "Element"), ("A_MessageAlert02", "messageAlert"),
                         ("A_MessageReticule", "messageReticule"), ("A_TutoMessage", "messageTuto")):
        assert any(w["name"] == widget for w in areas[name]["widgets"]), (name, widget)
    assert "P_SpectatorMode" in manifest["packages"]["Hud"]["pages"]
    table = json.loads((ui / "strings.json").read_text(encoding="utf-8"))
    assert table["MagmaDynamic"]["HUD"]["TextChat1"]
    # Key names for the key help texts (EInputKey 236 is the mouse wheel up).
    assert table["Engine"]["BaseGUIController"]["LocalizedKeyName[236]"] == "Mouse Wheel Up"


def test_int_values_lose_their_quotes():
    from svmbuild.uiconv import strings

    table = strings.parse_int(b'[Browser_Keys]\r\nKeyLabel[1]="Forward"\r\nPlain=Hacking\r\nInner=Say "hi"\r\n')
    assert table == {"Browser_Keys": {"KeyLabel[1]": "Forward", "Plain": "Hacking", "Inner": 'Say "hi"'}}


@pytest.mark.skipif(not (GAME / "System" / "_PC_" / "GUI.int").exists(), reason="reference install not present")
def test_reference_binding_labels_are_english(tmp_path, capsys):
    out = tmp_path / "content"
    assert cli.main(["ui", "--game", str(GAME), "--out", str(out)]) == 0
    capsys.readouterr()
    table = json.loads((out / "shared" / "ui" / "strings.json").read_text(encoding="utf-8"))
    keys = table["GUI"]["Browser_Keys"]
    assert keys["KeyLabel[1]"] == "Forward" and keys["KeyLabel[14]"] == "Switch Vision"
    assert table["GUI"]["ExtraKeyRows"]["ChatLabel"] == "Chat"
