"""The map catalog data (svmbuild/mapcatalog.py, step maps-catalog): the original's map list rules, Reloaded's
defaults read from a synthetic PE built the way Reloaded.Core.dll builds them, and the reference install."""

from __future__ import annotations

import json
import struct

import pytest
from svmbuild import cli, mapcatalog
from svmbuild.paths import default_game_root

BASE = 0x10000000
TEXT_VA = 0x1000
RDATA_VA = 0x2000


class _Code:
    """x86 that builds strings as the Reloaded initialisers do: lea ecx, [ebp - slot]; push len; push str; call."""

    def __init__(self):
        self.rdata = bytearray()
        self.text = bytearray()
        self._addr: dict[str, int] = {}

    def s(self, text: str) -> int:
        if text not in self._addr:
            self._addr[text] = BASE + RDATA_VA + len(self.rdata)
            self.rdata += text.encode("ascii") + b"\0"
            while len(self.rdata) % 4:
                self.rdata += b"\0"
        return self._addr[text]

    def build(self, text: str, slot: int) -> None:
        self.text += b"\x8d\x8d" + struct.pack("<i", -slot)            # lea ecx, [ebp - slot]
        self.text += b"\x6a" + bytes((len(text),))                      # push len
        self.text += b"\x68" + struct.pack("<I", self.s(text))          # push str
        self.text += b"\xe8" + struct.pack("<i", 0)                     # call
        self.text += b"\x90" * 4

    def pair(self, code: str, name: str, slot: int) -> None:
        """A pair constructor: both strings pushed (name first, code last) into one slot."""
        self.text += b"\x68" + struct.pack("<I", self.s(name))
        self.text += b"\x68" + struct.pack("<I", self.s(code))
        self.text += b"\x8d\x8d" + struct.pack("<i", -slot)
        self.text += b"\xe8" + struct.pack("<i", 0)

    def push(self, text: str) -> None:
        self.text += b"\x68" + struct.pack("<I", self.s(text)) + b"\x90" * 8

    def gap(self, n: int = 0x400) -> None:
        self.text += b"\x90" * n

    def pe(self) -> bytes:
        def pad(b: bytes, n: int = 0x200) -> bytes:
            return bytes(b) + b"\0" * (-len(b) % n)

        text, rdata = pad(self.text), pad(self.rdata)
        pe_off = 0x80
        head = bytearray(0x400)
        head[0:2] = b"MZ"
        struct.pack_into("<I", head, 0x3C, pe_off)
        head[pe_off:pe_off + 4] = b"PE\0\0"
        struct.pack_into("<HH", head, pe_off + 4, 0x14C, 2)
        struct.pack_into("<H", head, pe_off + 20, 0xE0)
        opt = pe_off + 24
        struct.pack_into("<H", head, opt, 0x10B)
        struct.pack_into("<I", head, opt + 28, BASE)
        sec = opt + 0xE0
        for i, (name, va, raw, data) in enumerate(((b".text", TEXT_VA, 0x400, text),
                                                   (b".rdata", RDATA_VA, 0x400 + len(text), rdata))):
            o = sec + 40 * i
            head[o:o + 8] = name.ljust(8, b"\0")
            struct.pack_into("<IIII", head, o + 8, len(data), va, len(data), raw)
        assert len(text) <= RDATA_VA - TEXT_VA
        return bytes(head) + text + rdata


def _reloaded() -> bytes:
    c = _Code()
    shared = ["MissE", "SancE", "AquaE", "ClubE", "CineE", "FactE", "KrauE", "MounE", "MuseD", "PolaE", "PrisD"]
    for i, m in enumerate(shared):
        c.build(m, 0x200 - i * 0x18)
    c.gap(0x20)
    c.push("Standard")
    c.push("The first synthetic playlist of the test.")
    c.push("Classic")
    c.push("The second synthetic playlist of the test.")
    for m in ("AquaD", "BankD", "ClubD"):
        c.push(m)
    c.push("Merc Deathmatch")
    c.push("The third synthetic playlist of the test.")
    c.gap(0x200)
    # The name table: code first, name first (built into reversed slots), then pair constructors.
    c.build("AquaD", 0x1000)
    c.build("Aquarius", 0x1000 - 0x18)
    c.build("Enhanced Aquarius", 0x1000 - 0x48)
    c.build("AquaE", 0x1000 - 0x30)
    c.pair("MounS", "Enh Mount Hospital Sabo", 0x900)
    c.pair("SlauS", "Snake Slaughterhouse", 0x8d0)
    c.push("AQU03")
    c.gap(0x800)
    for w in ("Enhanced", "Enh", "Phoenix's", "Snake", "UMP", "PS2", "Covert"):
        c.push(w)
    c.gap(0x200)
    c.build("VertX", 0x40)
    c.build("Enh Vertigo Plaza Extraction", 0x28)
    return c.pe()


def test_reloaded_defaults_read_from_the_initialisers():
    d = mapcatalog.read_reloaded_defaults(_reloaded())
    assert [(p["name"], p["gameMode"], len(p["maps"])) for p in d.playlists] == [
        ("Standard", "Story", 11), ("Classic", "Story", 3), ("Merc Deathmatch", "MercDeathMatch", 11)]
    assert d.playlists[0]["maps"][0] == "MissE"
    assert d.playlists[1]["maps"] == ["AquaD", "BankD", "ClubD"]
    assert d.playlists[1]["description"] == "The second synthetic playlist of the test."
    assert d.names == {"AquaD": "Aquarius", "AquaE": "Enhanced Aquarius", "MounS": "Enh Mount Hospital Sabo",
                       "SlauS": "Snake Slaughterhouse", "VertX": "Enh Vertigo Plaza Extraction"}
    assert d.variant_words == ["Enhanced", "Enh", "Phoenix's", "Snake", "UMP", "PS2", "Covert"]


def test_not_a_reloaded_dll_is_refused():
    with pytest.raises(ValueError):
        mapcatalog.read_reloaded_defaults(b"not a dll")
    with pytest.raises(ValueError):
        mapcatalog.read_reloaded_defaults(_Code().pe())


def test_original_map_list_skips_menu_and_tutorial_maps():
    files = ["ZiopD.sdc", "aquad.sdc", "Entry.sdc", "Lobby.sdc", "TSP20.sdc", "TME30.sdc", "TSP2.sdc", "AquaE.sdc",
             "notes.txt", "SteeE.sds"]
    assert mapcatalog.original_map_order(files) == ["aquad", "AquaE", "SteeE", "TSP2", "ZiopD"]
    assert mapcatalog.training_maps(files) == {"spy": ["TSP20"], "merc": ["TME30"]}
    assert mapcatalog.wildcard_match("TSP**.sd*", "tsp29.SDC")
    assert not mapcatalog.wildcard_match("TSP**.sd*", "TSP299.sdc")


def test_step_without_reloaded_writes_the_map_list(tmp_path, capsys):
    game = tmp_path / "game"
    (game / "Packages" / "Maps").mkdir(parents=True)
    (game / "Packages" / "Textures").mkdir(parents=True)
    for f in ("AquaD.sdc", "Entry.sdc", "TSP20.sdc"):
        (game / "Packages" / "Maps" / f).write_bytes(b"x")
    out = tmp_path / "out"
    assert cli.main(["--game", str(game), "--out", str(out), "maps-catalog"]) == 0
    doc = json.loads((out / "shared" / "maps" / "catalog.json").read_text(encoding="utf-8"))
    assert doc["originalOrder"] == ["AquaD"]
    assert doc["menuMaps"] == ["Entry"]
    assert doc["training"] == {"merc": [], "spy": ["TSP20"]}
    assert doc["playlists"] == [] and doc["names"] == {}


@pytest.mark.skipif(not (default_game_root() / "System" / "Reloaded.Core.dll").is_file(),
                    reason="reference install not present")
def test_reference_install_playlists_names_and_pictures():
    game = default_game_root()
    d = mapcatalog.read_reloaded_defaults((game / "System" / "Reloaded.Core.dll").read_bytes())
    assert [(p["name"], len(p["maps"])) for p in d.playlists] == [
        ("Standard", 15), ("Extended", 26), ("Classic", 13), ("3v3", 7), ("Merc Deathmatch", 15)]
    assert d.playlists[4]["maps"] == d.playlists[0]["maps"]
    assert d.names["AquaE"] == "Enhanced Aquarius" and d.names["VertX"] == "Enh Vertigo Plaza Extraction"
    assert len(d.names) >= 70
    files = [p.name for p in (game / "Packages" / "Maps").iterdir()]
    order = mapcatalog.original_map_order(files)
    assert len(order) == 73 and "Lobby" not in order and not any(m.startswith(("TSP", "TME")) for m in order)
