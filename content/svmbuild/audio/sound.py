"""Sound exports of the .uax packages (spec/native/audio.md, "Sound objects").

SCCT Versus does not use the stock UE2 USound layout. After the tagged properties (in practice only None):

    FName    FileType      "wav" for a sound with its own wave or stream, None for the list kinds
    FString  Source        the stream entry name in the map bank (or the original file name); XOR-obfuscated like
                           the name table in obfuscated packages
    u32      Flags
    f32      Gain          only when Flags has FLAG_GAIN: a linear factor (dB values converted by the tool)
    f32      Radius        only from package version 173 on (earlier: 0); replaces the radius of a PlaySound call
                           when above 0
    then by kind:
      random, switch, sequence   u32 count, then count x (FName sound, u32 value)
      multichannel               u32 channel mask (FL FR BL BR Center LFE from bit 0)
      streamed                   nothing (the Ogg Vorbis data is in the map's .uas bank)
      wave                       i32 skip offset, compact size, size bytes of RIFF WAVE
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field

FLAG_LOOP = 0x0002
FLAG_STREAM = 0x0004
FLAG_MUSIC = 0x0008          # runtime flag (PlayMusic): no Gain, no 3D, music volume
FLAG_2D = 0x0010
FLAG_UPDATE_PITCH = 0x0020   # runtime flag (ambient sounds)
FLAG_FIXED = 0x0040          # runtime flag: the source stops following its actor
FLAG_SWITCH = 0x0080
FLAG_RANDOM = 0x0100
FLAG_GAIN = 0x0400
FLAG_SEQUENCE = 0x0800
FLAG_TRANSIENT = 0x1000      # runtime flag: Serialize skips the sound data of such an object
FLAG_MULTICHANNEL = 0x2000
FLAG_STREAM_STAT = 0x4000    # only selects a statistics counter for streamed sounds

LIST_FLAGS = FLAG_SWITCH | FLAG_RANDOM | FLAG_SEQUENCE
RADIUS_VERSION = 173         # first package version that stores Radius
NAME_ENTRY_VERSION = 167     # list entries are FNames from this version on (strings before)

CHANNELS = ("FL", "FR", "BL", "BR", "Center", "LFE")


class SoundError(ValueError):
    pass


@dataclass
class ListEntry:
    name: str        # object name of the sound it plays
    value: int       # random: weight out of 10000; switch: surface type; sequence: repeat count (+0x2000 bit)


@dataclass
class WaveInfo:
    format_tag: int
    channels: int
    rate: int
    bits: int
    block_align: int
    data_bytes: int
    loops: list[tuple[int, int]] = field(default_factory=list)   # smpl loop start, end (sample frames)

    @property
    def frames(self) -> int:
        return self.data_bytes // self.block_align if self.block_align else 0

    @property
    def duration(self) -> float:
        return self.frames / self.rate if self.rate else 0.0


@dataclass
class SoundObject:
    path: str                      # Package.Group.Name
    name: str
    package: str
    version: int
    file_type: str
    source: str
    flags: int
    gain: float = 1.0
    radius: float = 0.0
    entries: list[ListEntry] = field(default_factory=list)
    channel_mask: int = 0
    data: bytes | None = None
    wave: WaveInfo | None = None
    props: dict = field(default_factory=dict)

    @property
    def kind(self) -> str:
        return kind_of(self.flags)

    @property
    def stream_name(self) -> str:
        """The bank entry name of a streamed or multichannel sound."""
        if self.flags & FLAG_MULTICHANNEL:
            return self.name
        return self.source or self.name


def kind_of(flags: int) -> str:
    if flags & FLAG_RANDOM:
        return "random"
    if flags & FLAG_SWITCH:
        return "switch"
    if flags & FLAG_SEQUENCE:
        return "sequence"
    if flags & FLAG_MULTICHANNEL:
        return "multichannel"
    if flags & FLAG_STREAM:
        return "stream"
    return "wave"


def decode_payload(r, version: int, path: str = "") -> dict:
    """Decode the part after the tagged properties from a scctpkg Reader positioned there."""
    out: dict = {"fileType": r.name(), "source": r.fstring()}
    flags = r.u32()
    out["flags"] = flags
    out["gain"] = r.f32() if flags & FLAG_GAIN else 1.0
    out["radius"] = r.f32() if version >= RADIUS_VERSION else 0.0
    if flags & LIST_FLAGS:
        if version < NAME_ENTRY_VERSION:
            raise SoundError(f"{path}: list entries stored as strings (package version {version})")
        n = r.u32()
        if n > 4096:
            raise SoundError(f"{path}: {n} list entries")
        out["entries"] = [ListEntry(r.name(), r.u32()) for _ in range(n)]
    elif flags & FLAG_MULTICHANNEL:
        out["channelMask"] = r.u32()
    elif not flags & FLAG_STREAM:
        r.i32()   # skip offset of the lazy array: absolute in some versions, 0 in others
        n = r.compact()
        if n < 0 or n > r.remaining():
            raise SoundError(f"{path}: wave size {n} with {r.remaining()} bytes left")
        out["data"] = r.raw(n)
    return out


def decode_sound(pkg, export) -> SoundObject:
    """One Sound export of a scctpkg Package."""
    from scctpkg.properties import read_properties  # noqa: PLC0415 - vendored reader, imported on demand

    r = pkg.export_reader(export)
    props = read_properties(r)
    path = export.full_name
    p = decode_payload(r, pkg.version, path)
    if r.remaining():
        raise SoundError(f"{path}: {r.remaining()} bytes left after the sound data")
    snd = SoundObject(path=path, name=export.name, package=pkg.path.stem, version=pkg.version,
                      file_type=p["fileType"], source=p["source"], flags=p["flags"], gain=p.get("gain", 1.0),
                      radius=p.get("radius", 0.0), entries=p.get("entries", []), channel_mask=p.get("channelMask", 0),
                      data=p.get("data"), props={k: v for k, v in props.items()})
    if snd.data is not None:
        snd.wave = wave_info(snd.data, path)
    return snd


def wave_info(data: bytes, where: str = "") -> WaveInfo:
    """fmt, data size and smpl loops of a RIFF WAVE; every chunk must fit in the RIFF size."""
    if len(data) < 12 or data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise SoundError(f"{where}: not a RIFF WAVE")
    size = struct.unpack_from("<I", data, 4)[0]
    if size + 8 != len(data):
        raise SoundError(f"{where}: RIFF size {size + 8} for {len(data)} bytes")
    fmt = None
    data_bytes = None
    loops: list[tuple[int, int]] = []
    p = 12
    while p + 8 <= len(data):
        cid = data[p:p + 4]
        n = struct.unpack_from("<I", data, p + 4)[0]
        body = p + 8
        if body + n > len(data):
            raise SoundError(f"{where}: chunk {cid!r} runs past the end")
        if cid == b"fmt ":
            tag, ch, rate, _, align, bits = struct.unpack_from("<HHIIHH", data, body)
            fmt = (tag, ch, rate, bits, align)
        elif cid == b"data":
            data_bytes = n
        elif cid == b"smpl" and n >= 36:
            count = struct.unpack_from("<I", data, body + 28)[0]
            for i in range(count):
                q = body + 36 + i * 24
                if q + 24 > body + n:
                    break
                start, end = struct.unpack_from("<II", data, q + 8)
                loops.append((start, end))
        p = body + n + (n & 1)
    if p != len(data):
        raise SoundError(f"{where}: {len(data) - p} bytes after the last chunk")
    if fmt is None or data_bytes is None:
        raise SoundError(f"{where}: no fmt or data chunk")
    tag, ch, rate, bits, align = fmt
    return WaveInfo(tag, ch, rate, bits, align, data_bytes, loops)


def channel_names(mask: int) -> list[str]:
    return [c for i, c in enumerate(CHANNELS) if mask & (1 << i)]
