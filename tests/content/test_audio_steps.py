"""The audio steps end to end: a synthetic install (one hand-built .uax, a .mux and a map bank) through the CLI and
the map writer, and the reference install when it is present."""

from __future__ import annotations

import json
import struct

import pytest
from svmbuild import cli
from svmbuild.audio import build, sound
from svmbuild.paths import default_game_root
from svmbuild.sources.env import import_formats

from test_audio_formats import _bank, _riff, _stream

import_formats()   # puts re/formats (scctformats._writer) on the path

NAMES = ["None", "Core", "Engine", "Class", "Package", "Sound", "wav", "Grp", "Hit_1", "Hit_2", "Ra_Hit", "Amb_Loop",
         "MC_Street", "Remove"]


def _sound_body(kind: str) -> bytes:
    from scctformats._writer import Writer  # noqa: PLC0415
    n = NAMES.index
    w = Writer()
    w.name(0)                                         # no tagged properties
    if kind == "Hit_1":
        riff = _riff(frames=441)
        w.name(n("wav"), 5).fstring("Hit_1").u32(sound.FLAG_GAIN).f32(2.0).f32(30.0).i32(0).compact(len(riff)).raw(riff)
    elif kind == "Hit_2":
        riff = _riff(frames=882, loop=(0, 800))
        w.name(n("wav"), 5).fstring("Hit_2").u32(sound.FLAG_LOOP).f32(0.0).i32(0).compact(len(riff)).raw(riff)
    elif kind == "Ra_Hit":
        w.name(0).fstring("").u32(sound.FLAG_RANDOM).f32(0.0).u32(3)
        w.name(n("Hit_1")).u32(5000).name(n("Hit_2")).u32(4000).name(n("Remove")).u32(1000)
    elif kind == "Amb_Loop":
        w.name(n("wav"), 5).fstring("Amb_Loop").u32(sound.FLAG_STREAM | sound.FLAG_2D | sound.FLAG_GAIN).f32(0.5).f32(0.0)
    elif kind == "MC_Street":
        w.name(0).fstring("").u32(sound.FLAG_MULTICHANNEL | sound.FLAG_STREAM | sound.FLAG_2D).f32(0.0).u32(0x0F)
    return w.bytes()


def _uax(version: int = 300) -> bytes:
    """A plain (not obfuscated) package: imports for Engine.Sound and Core.Package, a group and five sounds."""
    from scctformats._writer import Writer  # noqa: PLC0415
    n = NAMES.index
    names = Writer()
    for s in NAMES:
        data = s.encode("latin-1") + b"\0"
        names.compact(len(data)).raw(data).u32(0)
    imports = Writer()
    imports.name(n("Core")).name(n("Package")).i32(0).name(n("Engine"))       # -1 package Engine
    imports.name(n("Core")).name(n("Class")).i32(-1).name(n("Sound"))         # -2 class Engine.Sound
    imports.name(n("Core")).name(n("Package")).i32(0).name(n("Core"))         # -3 package Core
    imports.name(n("Core")).name(n("Class")).i32(-3).name(n("Package"))       # -4 class Core.Package
    bodies = [b"\x00"] + [_sound_body(k) for k in ("Hit_1", "Hit_2", "Ra_Hit", "Amb_Loop", "MC_Street")]
    head = 36
    names_off = head
    imports_off = names_off + len(names.bytes())
    data_off = imports_off + len(imports.bytes())
    offsets = []
    pos = data_off
    for b in bodies:
        offsets.append(pos)
        pos += len(b)
    exports = Writer()
    exports.compact(-4).compact(0).i32(0).name(n("Grp")).u32(0x70004).compact(len(bodies[0])).compact(offsets[0])
    for i, k in enumerate(("Hit_1", "Hit_2", "Ra_Hit", "Amb_Loop", "MC_Street"), start=1):
        exports.compact(-2).compact(0).i32(1).name(n(k)).u32(0xF0004).compact(len(bodies[i])).compact(offsets[i])
    exports_off = pos
    header = struct.pack("<IHHI6i", 0x9E2A83C1, version, 0, 1, len(NAMES), names_off, 6, exports_off, 4, imports_off)
    out = header + names.bytes() + imports.bytes() + b"".join(bodies) + exports.bytes()
    return out


def _game(tmp_path):
    game = tmp_path / "game"
    snd = game / "Packages" / "Sounds"
    snd.mkdir(parents=True)
    (snd / "Synth.uax").write_bytes(_uax())
    (snd / "MC_Street.mux").write_bytes(b"[Channels]\r\nFL=Street_L\r\nFR=Street_R\r\nBL=\r\nBR=\r\nCenter=\r\nLFE=\r\n")
    return game


def _tree(root):
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}


def test_synthetic_library_through_the_cli(tmp_path, capsys):
    game = _game(tmp_path)
    out = tmp_path / "content"
    args = ["audio", "--game", str(game), "--out", str(out)]
    assert cli.main(args) == 0
    assert "audio         -            built" in capsys.readouterr().out
    shared = out / "shared" / "audio"
    lib = json.loads((shared / "audio.json").read_text(encoding="utf-8"))
    assert lib["format"] == "svm.audio/1" and lib["packages"]["Synth"]["sounds"] == 5
    s = lib["sounds"]
    assert s["Synth.Grp.Hit_1"]["kind"] == "wave" and s["Synth.Grp.Hit_1"]["volume"] == 2.0
    assert s["Synth.Grp.Hit_1"]["radius"] == 30.0 and s["Synth.Grp.Hit_1"]["file"] == "wav/Synth/Grp/Hit_1.wav"
    assert s["Synth.Grp.Hit_2"]["loop"] is True and (s["Synth.Grp.Hit_2"]["loopStart"], s["Synth.Grp.Hit_2"]["loopEnd"]) == (0, 800)
    assert [(e["sound"], e["value"]) for e in s["Synth.Grp.Ra_Hit"]["entries"]] == \
        [("Synth.Grp.Hit_1", 5000), ("Synth.Grp.Hit_2", 4000), (None, 1000)]
    assert s["Synth.Grp.Amb_Loop"] == {"flags": 0x414, "kind": "stream", "loop": False, "pitch": 1.0, "positional": False,
                                       "radius": 0.0, "stream": "Amb_Loop", "volume": 0.5}
    assert s["Synth.Grp.MC_Street"]["channelMap"]["FL"] == "Street_L" and s["Synth.Grp.MC_Street"]["speakers"] == \
        ["FL", "FR", "BL", "BR"]
    assert lib["unresolved"] == ["Synth.Grp.Ra_Hit -> Remove"]
    assert (shared / "wav" / "Synth" / "Grp" / "Hit_1.wav").read_bytes() == _riff(frames=441)
    first = _tree(shared)
    assert cli.main(args) == 0
    assert "cached" in capsys.readouterr().out
    assert cli.main(args + ["--force"]) == 0
    assert _tree(shared) == first   # byte-identical rebuild


def test_synthetic_map_bank(tmp_path, capsys):
    game = _game(tmp_path)
    out = tmp_path / "content"
    assert cli.main(["audio", "--game", str(game), "--out", str(out)]) == 0
    capsys.readouterr()
    # A bank for map "MapX" whose packages are PkgA and PkgB (from the format tests), plus our Synth section.
    data, _ = _bank()
    (game / "Packages" / "Sounds" / "MapX.uas").write_bytes(data)
    lib = build.load_library(out / "shared" / "audio")
    audio = tmp_path / "MapX" / "audio"
    stats = build.build_map_audio(game, "MapX", audio, lib, src=None)
    assert stats["streams"] == 3 and stats["badCrcPages"] == 0 and stats["noBank"] is False
    assert sorted(build.bank_layout(game, "MapX")) == sorted(p.relative_to(audio).as_posix() for p in audio.rglob("*")
                                                              if p.is_file())
    doc = json.loads((audio / "audio.json").read_text(encoding="utf-8"))
    assert doc["format"] == "svm.mapaudio/1" and [s["name"] for s in doc["streams"]] == ["Loop_A", "Shot_B", "Music"]
    assert doc["streams"][0]["duration"] == pytest.approx(2.0) and doc["streams"][0]["serial"] == 11
    assert (audio / "streams" / "PkgA" / "Shot_B.ogg").read_bytes() == _stream(serial=12, pages=2)
    # No Synth section in this bank: its streamed sounds are not part of the map.
    assert "Synth.Grp.Amb_Loop" not in doc["sounds"]


def test_missing_sounds_folder_fails(tmp_path, capsys):
    game = tmp_path / "game"
    (game / "Packages" / "Sounds").mkdir(parents=True)
    assert cli.main(["audio", "--game", str(game), "--out", str(tmp_path / "content")]) == 1
    assert "no .uax packages" in capsys.readouterr().out


GAME = default_game_root()


@pytest.mark.skipif(not (GAME / "Packages" / "Sounds" / "SteeE.uas").exists(), reason="reference install not present")
def test_reference_install(tmp_path, capsys):
    out = tmp_path / "content"
    assert cli.main(["audio", "--game", str(GAME), "--out", str(out)]) == 0
    capsys.readouterr()
    lib = build.load_library(out / "shared" / "audio")
    kinds = {}
    for v in lib["sounds"].values():
        kinds[v["kind"]] = kinds.get(v["kind"], 0) + 1
    assert len(lib["sounds"]) == 1513 and kinds["wave"] == 1078 and len(lib["mux"]) == 18
    assert lib["problems"] == [] and len(lib["duplicates"]) == 7
    assert lib["classes"]["SBase.SWeaponDefense"]["FireSingleShotSound"] == "Armes_impacts.Armes.RA_GunDef_1shot"
    audio = tmp_path / "SteeE" / "audio"
    stats = build.build_map_audio(GAME, "SteeE", audio, lib, src=None)
    assert stats["streams"] == 22 and stats["badCrcPages"] == 0 and stats["bankProblems"] == 0
    doc = json.loads((audio / "audio.json").read_text(encoding="utf-8"))
    music = doc["sounds"]["Amb_Squat.Ambiances.Squ_Mus_Stress"]
    assert music["root"] == "map" and music["loop"] is True and music["duration"] == pytest.approx(235.8, abs=0.1)
