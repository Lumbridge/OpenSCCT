"""Little-endian writer for UE2 primitives. Used to build synthetic test data and to re-encode."""

from __future__ import annotations

import struct


def compact(v: int) -> bytes:
    """UE2 FCompactIndex: sign bit 0x80 and more bit 0x40 in the first byte (6 value bits), then 7-bit groups."""
    neg = v < 0
    v = -v if neg else v
    out = bytearray()
    b = v & 0x3F
    v >>= 6
    if neg:
        b |= 0x80
    if v:
        b |= 0x40
    out.append(b)
    while v:
        b = v & 0x7F
        v >>= 7
        if v:
            b |= 0x80
        out.append(b)
    return bytes(out)


class Writer:
    def __init__(self) -> None:
        self.b = bytearray()

    def bytes(self) -> bytes:
        return bytes(self.b)

    def raw(self, data: bytes) -> "Writer":
        self.b += data
        return self

    def u8(self, v: int) -> "Writer":
        self.b += struct.pack("<B", v)
        return self

    def u16(self, v: int) -> "Writer":
        self.b += struct.pack("<H", v)
        return self

    def i16(self, v: int) -> "Writer":
        self.b += struct.pack("<h", v)
        return self

    def i32(self, v: int) -> "Writer":
        self.b += struct.pack("<i", v)
        return self

    def u32(self, v: int) -> "Writer":
        self.b += struct.pack("<I", v)
        return self

    def f32(self, v: float) -> "Writer":
        self.b += struct.pack("<f", v)
        return self

    def vec3(self, v) -> "Writer":
        self.b += struct.pack("<3f", *v)
        return self

    def compact(self, v: int) -> "Writer":
        self.b += compact(v)
        return self

    def name(self, local: int, global_index: int = 0) -> "Writer":
        """SCCT name reference: package-local compact index, then the cooked global compact index."""
        return self.compact(local).compact(global_index)

    def fstring(self, s: str) -> "Writer":
        """Plain (not obfuscated) ANSI FString with its NUL."""
        if not s:
            return self.compact(0)
        data = s.encode("latin1") + b"\0"
        return self.compact(len(data)).raw(data)
