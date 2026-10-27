"""Map stream banks (.uas): the Ogg Vorbis data of every streamed sound a map can play (spec/native/audio.md).

One bank per map, named like the map (Packages/Sounds/<Map>.uas). Little-endian throughout:

    u32 package count
    count x { char name[64]; u32 section offset }     package of the .uax that owns the streamed sounds
    at each section offset:
        u32 entry count
        count x { char name[64]; u32 start; u32 start again; u32 end; u32 one }
    stream bytes: one complete Ogg Vorbis stream per entry, [start, end)

Names are NUL-terminated inside their 64 bytes; the bytes after the NUL are leftover memory and mean nothing. The
writer lays out section, then that section's streams, then the next section, but some banks also hold stale bytes
(an older index and a cut page) between two sections: only the offsets are trusted, and those bytes are reported as
unused ranges. The index has no duration; it comes from the stream (last granule position over the sample rate).
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field

from . import ogg

NAME_BYTES = 64
PACKAGE_RECORD = NAME_BYTES + 4
ENTRY_RECORD = NAME_BYTES + 16


class BankError(ValueError):
    pass


@dataclass
class BankEntry:
    package: str
    name: str
    start: int
    start2: int
    end: int
    flag: int
    stream: ogg.OggInfo | None = None

    @property
    def length(self) -> int:
        return self.end - self.start


@dataclass
class Bank:
    size: int
    packages: list[tuple[str, int]]
    entries: list[BankEntry]
    unused: list[tuple[int, int]] = field(default_factory=list)
    problems: list[str] = field(default_factory=list)

    def entry(self, package: str, name: str) -> BankEntry | None:
        pk, nm = package.lower(), name.lower()
        for e in self.entries:
            if e.package.lower() == pk and e.name.lower() == nm:
                return e
        return None


def _name(raw: bytes) -> str:
    return raw.split(b"\0", 1)[0].decode("latin-1")


def read_bank(data: bytes, check_streams: bool = True) -> Bank:
    """Parse a bank; with check_streams every entry's pages are walked and their CRCs checked."""
    n = len(data)
    if n < 4:
        raise BankError("bank shorter than its package count")
    count = struct.unpack_from("<I", data, 0)[0]
    head_end = 4 + count * PACKAGE_RECORD
    if count > 256 or head_end > n:
        raise BankError(f"bad package count {count}")
    packages = []
    for i in range(count):
        o = 4 + i * PACKAGE_RECORD
        packages.append((_name(data[o:o + NAME_BYTES]), struct.unpack_from("<I", data, o + NAME_BYTES)[0]))
    used: list[tuple[int, int]] = [(0, head_end)]
    entries: list[BankEntry] = []
    problems: list[str] = []
    for pkg, off in packages:
        if off + 4 > n:
            raise BankError(f"section of {pkg} at {off} is past the end ({n})")
        m = struct.unpack_from("<I", data, off)[0]
        sec_end = off + 4 + m * ENTRY_RECORD
        if m > 100000 or sec_end > n:
            raise BankError(f"section of {pkg}: bad entry count {m}")
        used.append((off, sec_end))
        for k in range(m):
            o = off + 4 + k * ENTRY_RECORD
            start, start2, end, flag = struct.unpack_from("<4I", data, o + NAME_BYTES)
            e = BankEntry(pkg, _name(data[o:o + NAME_BYTES]), start, start2, end, flag)
            if not (0 < start < end <= n):
                raise BankError(f"{pkg}.{e.name}: stream range [{start}, {end}) outside the bank ({n})")
            if start2 != start:
                problems.append(f"{pkg}.{e.name}: second start {start2} differs from {start}")
            if flag != 1:
                problems.append(f"{pkg}.{e.name}: last index word {flag}")
            entries.append(e)
            used.append((start, end))
    used.sort()
    unused = []
    pos = 0
    for a, b in used:
        if a < pos:
            raise BankError(f"overlapping ranges at {a}")
        if a > pos:
            unused.append((pos, a))
        pos = b
    if pos < n:
        unused.append((pos, n))
    names = {}
    for e in entries:
        key = (e.package.lower(), e.name.lower())
        if key in names:
            problems.append(f"{e.package}.{e.name}: listed twice")
        names[key] = e
    bank = Bank(n, packages, entries, unused, problems)
    if check_streams:
        view = memoryview(data)
        for e in entries:
            e.stream = ogg.walk(view[e.start:e.end])
            if not e.stream.ok:
                bad = e.stream.errors[:3] + ([f"{e.stream.bad_crc} bad page CRCs"] if e.stream.bad_crc else [])
                problems.append(f"{e.package}.{e.name}: " + "; ".join(bad))
    return bank
