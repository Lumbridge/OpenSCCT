"""Ogg pages: walk a byte range page by page, check every page CRC and read the Vorbis identification header.

Ogg page header (RFC 3533): "OggS", version (0), header type (1 continued, 2 first page, 4 last page), granule
position (int64), serial number, page sequence number, CRC32, segment count, segment table, then the body. The CRC is
the Ogg polynomial 0x04C11DB7, MSB first, initial value 0, no final XOR, over the whole page with the CRC field zero.
"""

from __future__ import annotations

import struct
import zlib
from dataclasses import dataclass, field

CAPTURE = b"OggS"
HEADER = struct.Struct("<4sBBqIIIB")   # capture, version, type, granule, serial, sequence, crc, segments

FLAG_CONTINUED = 1
FLAG_FIRST = 2
FLAG_LAST = 4


def _table() -> list[int]:
    out = []
    for i in range(256):
        r = i << 24
        for _ in range(8):
            r = ((r << 1) ^ 0x04C11DB7) if r & 0x80000000 else (r << 1)
        out.append(r & 0xFFFFFFFF)
    return out


_CRC = _table()
_REVERSED_BITS = bytes(int(f"{i:08b}"[::-1], 2) for i in range(256))


def crc32_reference(data: bytes | memoryview, crc: int = 0) -> int:
    """The Ogg CRC byte by byte (MSB first). Slow; the definition the fast path is tested against."""
    t = _CRC
    for b in data:
        crc = ((crc << 8) & 0xFFFFFFFF) ^ t[((crc >> 24) ^ b) & 0xFF]
    return crc


def crc32(data: bytes | memoryview) -> int:
    """The Ogg CRC (initial value 0) through zlib: the MSB-first CRC of the data is the bit-reversed LSB-first CRC
    (polynomial 0xEDB88320, zlib's) of the data with every byte's bits reversed."""
    c = zlib.crc32(bytes(data).translate(_REVERSED_BITS), 0xFFFFFFFF) ^ 0xFFFFFFFF
    return int(f"{c:032b}"[::-1], 2)


def page_crc(page: bytes | memoryview) -> int:
    """CRC of a whole page as stored, computed with its CRC field (bytes 22..25) taken as zero."""
    page = bytes(page)
    return crc32(page[:22] + bytes(4) + page[26:])


@dataclass
class Page:
    offset: int          # from the start of the walked range
    size: int
    flags: int
    granule: int
    serial: int
    sequence: int
    crc_ok: bool


@dataclass
class OggInfo:
    """What a walk over one stream found."""
    pages: list[Page] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    channels: int = 0
    rate: int = 0
    nominal_bitrate: int = 0
    vendor: str = ""

    @property
    def bad_crc(self) -> int:
        return sum(1 for p in self.pages if not p.crc_ok)

    @property
    def serials(self) -> list[int]:
        return sorted({p.serial for p in self.pages})

    @property
    def samples(self) -> int:
        """Granule position of the last page that has one (Vorbis: PCM samples per channel)."""
        for p in reversed(self.pages):
            if p.granule >= 0:
                return p.granule
        return 0

    @property
    def duration(self) -> float:
        return self.samples / self.rate if self.rate else 0.0

    @property
    def ok(self) -> bool:
        return not self.errors and self.bad_crc == 0 and bool(self.pages)


def walk(data: bytes | memoryview) -> OggInfo:
    """Walk every page of one logical Ogg Vorbis stream that fills `data` exactly."""
    data = memoryview(data)
    info = OggInfo()
    p = 0
    n = len(data)
    first_body = None
    while p < n:
        if n - p < HEADER.size or bytes(data[p:p + 4]) != CAPTURE:
            info.errors.append(f"no page capture at {p}")
            break
        _, version, flags, granule, serial, seq, crc, nseg = HEADER.unpack_from(data, p)
        if version != 0:
            info.errors.append(f"page {len(info.pages)}: version {version}")
        seg_end = p + HEADER.size + nseg
        if seg_end > n:
            info.errors.append(f"page {len(info.pages)}: segment table past the end")
            break
        body = sum(data[p + HEADER.size:seg_end])
        end = seg_end + body
        if end > n:
            info.errors.append(f"page {len(info.pages)}: body past the end ({end - n} bytes short)")
            break
        page = data[p:end]
        info.pages.append(Page(p, end - p, flags, granule, serial, seq, page_crc(page) == crc))
        if first_body is None:
            first_body = bytes(data[seg_end:end])
        p = end
    if info.pages:
        if not info.pages[0].flags & FLAG_FIRST:
            info.errors.append("first page lacks the first-page flag")
        if not info.pages[-1].flags & FLAG_LAST:
            info.errors.append("last page lacks the last-page flag")
        for i, pg in enumerate(info.pages):
            if pg.sequence != i:
                info.errors.append(f"page {i}: sequence number {pg.sequence}")
                break
        if len(info.serials) != 1:
            info.errors.append(f"{len(info.serials)} serial numbers in one stream")
    _vorbis_id(first_body, info)
    return info


def _vorbis_id(body: bytes | None, info: OggInfo) -> None:
    # Identification header: type 1, "vorbis", version, channels, rate, max, nominal, min bitrates, blocksizes, framing.
    if body is None or len(body) < 30 or body[:7] != b"\x01vorbis":
        info.errors.append("first packet is not a Vorbis identification header")
        return
    _, channels, rate, _, nominal, _ = struct.unpack_from("<IBIiii", body, 7)
    info.channels = channels
    info.rate = rate
    info.nominal_bitrate = nominal
