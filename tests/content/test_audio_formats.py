"""The audio decoders on hand-built bytes: Ogg pages and CRCs, .uas banks, Sound payloads, RIFF WAVE facts, .mux maps,
and the manifest patch of the per-map audio files."""

from __future__ import annotations

import json
import os
import shutil
import struct
from pathlib import Path

import pytest
from svmbuild import canonical
from svmbuild.audio import build, mux, ogg, sound, uas
from svmbuild.sources.env import import_formats
from svmbuild.validation import validate_svmap

# ---- Ogg ---------------------------------------------------------------------------------------------------------


def _page(body: bytes, flags: int, granule: int, serial: int, seq: int, crc_ok: bool = True) -> bytes:
    segs = []
    n = len(body)
    while n >= 255:
        segs.append(255)
        n -= 255
    segs.append(n)
    head = struct.pack("<4sBBqIIIB", b"OggS", 0, flags, granule, serial, seq, 0, len(segs)) + bytes(segs)
    page = bytearray(head + body)
    crc = ogg.crc32_reference(bytes(page))
    struct.pack_into("<I", page, 22, crc if crc_ok else crc ^ 1)
    return bytes(page)


def _vorbis_id(channels: int = 2, rate: int = 22050) -> bytes:
    return b"\x01vorbis" + struct.pack("<IBIiii", 0, channels, rate, 0, 88000, 0) + b"\xa9\x01"


def _stream(serial: int = 7, samples: int = 44100, rate: int = 22050, bad_page: int = -1, pages: int = 3) -> bytes:
    out = _page(_vorbis_id(rate=rate), ogg.FLAG_FIRST, 0, serial, 0, bad_page != 0)
    for i in range(1, pages):
        last = i == pages - 1
        out += _page(bytes([i]) * (300 * i), ogg.FLAG_LAST if last else 0, samples if last else samples // 2, serial, i,
                     bad_page != i)
    return out


def test_fast_crc_matches_the_definition():
    for n in (0, 1, 3, 255, 4096):
        data = os.urandom(n)
        assert ogg.crc32(data) == ogg.crc32_reference(data)


def test_walk_reads_every_page_and_the_vorbis_header():
    info = ogg.walk(_stream(samples=66150))
    assert info.ok and len(info.pages) == 3 and info.bad_crc == 0
    assert (info.channels, info.rate, info.samples, info.serials) == (2, 22050, 66150, [7])
    assert info.duration == pytest.approx(3.0)


def test_walk_reports_bad_crc_and_broken_streams():
    bad = ogg.walk(_stream(bad_page=1))
    assert not bad.ok and bad.bad_crc == 1
    cut = ogg.walk(_stream()[:-10])
    assert not cut.ok and any("past the end" in e for e in cut.errors)
    two = ogg.walk(_stream(serial=1) + _stream(serial=2))
    assert any("serial" in e for e in two.errors)
    no_last = ogg.walk(_page(_vorbis_id(), ogg.FLAG_FIRST, 0, 9, 0))
    assert any("last page" in e for e in no_last.errors)


# ---- banks --------------------------------------------------------------------------------------------------------


def _bank(junk: bytes = b"", corrupt: bool = False) -> tuple[bytes, dict]:
    """Two packages; the first section's streams, then junk, then the second section and its stream."""
    streams = {("PkgA", "Loop_A"): _stream(serial=11), ("PkgA", "Shot_B"): _stream(serial=12, pages=2),
               ("PkgB", "Music"): _stream(serial=13, bad_page=2 if corrupt else -1)}
    head = 4 + 2 * uas.PACKAGE_RECORD

    def name64(s: str) -> bytes:
        return s.encode("latin-1") + b"\0" + b"\xcd" * (63 - len(s))   # leftover bytes after the NUL

    data = bytearray(head)
    struct.pack_into("<I", data, 0, 2)
    sections = {}
    for pi, pkg in enumerate(("PkgA", "PkgB")):
        if pkg == "PkgB":
            data += junk
        off = len(data)
        sections[pkg] = off
        entries = [k for k in streams if k[0] == pkg]
        index = bytearray(4 + len(entries) * uas.ENTRY_RECORD)
        struct.pack_into("<I", index, 0, len(entries))
        pos = off + len(index)
        for i, key in enumerate(entries):
            o = 4 + i * uas.ENTRY_RECORD
            index[o:o + 64] = name64(key[1])
            s = streams[key]
            struct.pack_into("<4I", index, o + 64, pos, pos, pos + len(s), 1)
            pos += len(s)
        data += index
        for key in entries:
            data += streams[key]
        o = 4 + pi * uas.PACKAGE_RECORD
        data[o:o + 64] = name64(pkg)
        struct.pack_into("<I", data, o + 64, off)
    return bytes(data), sections


def test_bank_index_streams_and_unused_bytes():
    data, sections = _bank(junk=b"\x08\x00\x00\x00stale index" + b"\x00" * 9)
    b = uas.read_bank(data)
    assert [p for p, _ in b.packages] == ["PkgA", "PkgB"] and dict(b.packages) == sections
    assert [(e.package, e.name) for e in b.entries] == [("PkgA", "Loop_A"), ("PkgA", "Shot_B"), ("PkgB", "Music")]
    assert b.problems == []
    assert b.unused == [(sections["PkgB"] - 24, sections["PkgB"])]
    e = b.entry("pkga", "SHOT_B")
    assert e is not None and e.stream.ok and len(e.stream.pages) == 2
    assert data[e.start:e.end] == _stream(serial=12, pages=2)


def test_bank_reports_a_bad_page_and_rejects_bad_offsets():
    data, _ = _bank(corrupt=True)
    b = uas.read_bank(data)
    assert len(b.problems) == 1 and "PkgB.Music" in b.problems[0] and "CRC" in b.problems[0]
    broken = bytearray(data)
    struct.pack_into("<I", broken, 4 + 64, len(data) + 10)
    with pytest.raises(uas.BankError):
        uas.read_bank(bytes(broken))


# ---- Sound payloads -----------------------------------------------------------------------------------------------

NAMES = ["None", "wav", "Hit_1", "Hit_2", "Seq_End"]


class FakePackage:
    def __init__(self, data: bytes, obfuscated: bool = False):
        self.data = data
        self.names = list(NAMES)
        self.names_obfuscated = obfuscated
        self.path = Path("Synth.uax")


def _writer():
    import_formats()
    from scctformats._writer import Writer  # noqa: PLC0415
    return Writer()


def _reader(data: bytes, obfuscated: bool = False):
    from scctpkg.package import Reader  # noqa: PLC0415
    return Reader(data, 0, FakePackage(data, obfuscated), base=0)


def _riff(frames: int = 100, rate: int = 22050, loop: tuple[int, int] | None = None) -> bytes:
    fmt = struct.pack("<HHIIHH", 1, 1, rate, rate * 2, 2, 16)
    body = b"fmt " + struct.pack("<I", len(fmt)) + fmt + b"data" + struct.pack("<I", frames * 2) + bytes(frames * 2)
    if loop is not None:
        smpl = struct.pack("<9I", 0, 0, 0, 60, 0, 0, 0, 1, 0) + struct.pack("<6I", 0, 0, loop[0], loop[1], 0, 0)
        body += b"smpl" + struct.pack("<I", len(smpl)) + smpl
    return b"RIFF" + struct.pack("<I", 4 + len(body)) + b"WAVE" + body


def _head(w, file_type: int, source: str, flags: int):
    w.name(file_type, 1234567)   # local + cooked global index
    w.fstring(source)
    w.u32(flags)


def test_wave_sound_with_gain_and_radius():
    riff = _riff(frames=2205, loop=(10, 2000))
    w = _writer()
    _head(w, 1, "Hit_1", sound.FLAG_GAIN)
    w.f32(0.5).f32(80.0).i32(0).compact(len(riff)).raw(riff)
    r = _reader(w.bytes())
    p = sound.decode_payload(r, 300, "Synth.Hit_1")
    assert r.remaining() == 0
    assert (p["fileType"], p["source"], p["flags"], p["gain"], p["radius"]) == ("wav", "Hit_1", 0x400, 0.5, 80.0)
    info = sound.wave_info(p["data"])
    assert (info.channels, info.rate, info.bits, info.frames, info.loops) == (1, 22050, 16, 2205, [(10, 2000)])
    assert info.duration == pytest.approx(0.1)
    assert sound.kind_of(p["flags"]) == "wave"


def test_version_172_has_no_radius_and_streams_have_no_data():
    w = _writer()
    _head(w, 1, "Amb_Loop", sound.FLAG_GAIN | sound.FLAG_STREAM | sound.FLAG_2D)
    w.f32(2.0)
    r = _reader(w.bytes())
    p = sound.decode_payload(r, 172)
    assert r.remaining() == 0 and p["radius"] == 0.0 and p["gain"] == 2.0 and "data" not in p
    assert sound.kind_of(p["flags"]) == "stream"


def test_list_and_multichannel_sounds():
    w = _writer()
    _head(w, 0, "", sound.FLAG_RANDOM)
    w.f32(0.0).u32(2).name(2, 99).u32(3333).name(3, 98).u32(5000)
    p = sound.decode_payload(_reader(w.bytes()), 175)
    assert [(e.name, e.value) for e in p["entries"]] == [("Hit_1", 3333), ("Hit_2", 5000)]
    w = _writer()
    _head(w, 0, "", sound.FLAG_SEQUENCE | sound.FLAG_GAIN)
    w.f32(1.0).f32(0.0).u32(1).name(4).u32(0x2005)
    p = sound.decode_payload(_reader(w.bytes()), 300)
    assert sound.kind_of(p["flags"]) == "sequence" and p["entries"][0].value == 0x2005
    w = _writer()
    _head(w, 0, "", sound.FLAG_MULTICHANNEL | sound.FLAG_STREAM | sound.FLAG_2D)
    w.f32(0.0).u32(0x0F)
    p = sound.decode_payload(_reader(w.bytes()), 175)
    assert p["channelMask"] == 15 and sound.channel_names(15) == ["FL", "FR", "BL", "BR"]
    assert sound.kind_of(p["flags"]) == "multichannel"


def test_obfuscated_source_string():
    w = _writer()
    w.name(1)
    text = b"Hit_1\0"
    start = len(w.bytes()) + 1          # after the compact length byte
    w.compact(len(text)).raw(bytes(c ^ ((start + i) & 0xFF) for i, c in enumerate(text)))
    w.u32(sound.FLAG_STREAM).f32(0.0)
    p = sound.decode_payload(_reader(w.bytes(), obfuscated=True), 300)
    assert p["source"] == "Hit_1"


def test_wave_info_rejects_bad_riff():
    good = _riff()
    with pytest.raises(sound.SoundError):
        sound.wave_info(good[:-4])
    with pytest.raises(sound.SoundError):
        sound.wave_info(b"RIFX" + good[4:])


def test_mux_channels():
    text = "[Channels]\r\nFL=Amb_L\r\nFR=Amb_R\r\nBL=Amb_Ls\r\nbr=Amb_Rs\r\nCenter=\r\nLFE=\r\n\r\n"
    assert mux.read_mux(text) == {"FL": "Amb_L", "FR": "Amb_R", "BL": "Amb_Ls", "BR": "Amb_Rs", "Center": "", "LFE": ""}


def test_output_names_are_safe():
    assert build.wav_rel("Pkg.Grp.Snd one") == "wav/Pkg/Grp/Snd_one.wav"
    assert build.stream_rel("Interface", "proto_COMBAT-1Mus_06_StressHigh10-") == \
        "streams/Interface/proto_COMBAT-1Mus_06_StressHigh10-.ogg"


# ---- manifest -----------------------------------------------------------------------------------------------------


def test_patch_manifest_keeps_the_map_valid(tmp_path, testbox_dir):
    root = tmp_path / "TestBox"
    shutil.copytree(testbox_dir, root)
    assert validate_svmap(root) == []
    (root / "audio" / "streams" / "PkgA").mkdir(parents=True)
    files = {"audio.json": b'{"format": "svm.mapaudio/1"}\n', "streams/PkgA/Loop_A.ogg": _stream()}
    listing = {}
    for rel, data in files.items():
        (root / "audio" / rel).write_bytes(data)
        listing[rel] = {"sha256": canonical.sha256_hex(data), "size": len(data)}
    assert build.patch_manifest(root, listing, "ab" * 32) is True
    assert build.patch_manifest(root, listing, "ab" * 32) is False   # already patched: unchanged
    m = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    assert {"pack": "audio", "sha256": "ab" * 32} in m["requires"] and "audio" in m["hashes"]
    assert validate_svmap(root) == []
    # The maps step's manifest back (audio files removed): the patch with no files drops the audio entries.
    shutil.rmtree(root / "audio")
    build.patch_manifest(root, {}, "ab" * 32)
    m = json.loads((root / "manifest.json").read_text(encoding="utf-8"))
    assert "audio" not in m["hashes"] and all(r["pack"] != "audio" for r in m["requires"])
    assert validate_svmap(root) == []
