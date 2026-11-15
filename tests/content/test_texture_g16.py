"""G16 projector decoding: byte order, luminance and formats specific to Versus."""

import struct
from types import SimpleNamespace

from svmbuild.paths import import_scctpkg

import_scctpkg()
from scctpkg import texture
from scctpkg.package import Reader, _encode_compact


def decode(monkeypatch, fmt, payload, width=4):
    monkeypatch.setattr(texture, "read_properties", lambda r: {"Format": fmt, "bAlphaTexture": True})
    monkeypatch.setattr(texture, "skip_state_frame", lambda r, flags: None)
    mip = b"\x01" + bytes(4) + _encode_compact(len(payload)) + payload + struct.pack("<iiBB", width, 1, 2, 0)
    pkg = SimpleNamespace(export_reader=lambda e: Reader(mip))
    return texture.read_texture(pkg, SimpleNamespace(name="Projector", flags=0))


def test_g16_little_endian_luminance_has_opaque_alpha(monkeypatch):
    tx = decode(monkeypatch, 10, struct.pack("<4H", 0, 255, 0x8001, 65535))
    assert list(tx.image.getdata()) == [(0, 0, 0, 255), (0, 0, 0, 255), (128, 128, 128, 255), (255, 255, 255, 255)]
    assert not tx.has_alpha
    assert tx.luminance16 == struct.pack("<4H", 0, 255, 0x8001, 65535)


def test_truncated_g16_is_undecodable(monkeypatch):
    tx = decode(monkeypatch, 10, bytes(7))
    assert tx.image is None
    assert tx.luminance16 is None


def test_versus_p4_is_not_single_player_dudv16(monkeypatch):
    assert decode(monkeypatch, 12, bytes(8)).image is None
