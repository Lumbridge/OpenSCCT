"""UTexture reader: decodes mip 0 of DXT1/3/5, RGBA8 and P8 textures to RGBA.

Serialized after the tagged properties (USize, VSize, Format, Palette ...):

    compact MipCount
    per mip: i32 LazySkip; compact DataSize; bytes[DataSize]; i32 USize; i32 VSize; u8 UBits; u8 VBits

Format enum (UE2 ETextureFormat): 0 P8, 1 RGBA7, 2 RGB16, 3 DXT1, 4 RGB8,
5 RGBA8, 6 NODATA, 7 DXT3, 8 DXT5, 9 L8, 10 G16, 11 RRRGGGBBB.
"""

from __future__ import annotations

import io
import struct
from dataclasses import dataclass
from typing import List, Optional

import numpy as np
from PIL import Image

from .package import Export, Package, PackageError
from .properties import read_properties, skip_state_frame

TEXF_P8 = 0
TEXF_DXT1 = 3
TEXF_RGB8 = 4
TEXF_RGBA8 = 5
TEXF_DXT3 = 7
TEXF_DXT5 = 8
TEXF_L8 = 9
TEXF_G16 = 10

_DDS_FOURCC = {TEXF_DXT1: b"DXT1", TEXF_DXT3: b"DXT3", TEXF_DXT5: b"DXT5"}


@dataclass
class Texture:
    name: str
    width: int
    height: int
    format: int
    image: Optional[Image.Image]   # RGBA PIL image of mip 0, None if undecodable
    props: dict
    has_alpha: bool = False


def _dds_bytes(fmt: int, w: int, h: int, data: bytes) -> bytes:
    """Wrap a DXT block payload in a minimal DDS header so Pillow can decode it."""
    block = 8 if fmt == TEXF_DXT1 else 16
    linear = max(1, (w + 3) // 4) * max(1, (h + 3) // 4) * block
    header = struct.pack(
        "<4sI I I I I I I 11I 8I 5I",
        b"DDS ", 124,
        0x1 | 0x2 | 0x4 | 0x1000 | 0x80000,  # caps, height, width, pixelformat, linearsize
        h, w, linear, 0, 0,
        *([0] * 11),
        32, 0x4, struct.unpack("<I", _DDS_FOURCC[fmt])[0], 0, 0, 0, 0, 0,  # pixel format
        0x1000, 0, 0, 0, 0,
    )
    return header + data[:linear]


def _read_palette(pkg: Package, ref: int) -> Optional[np.ndarray]:
    if ref == 0:
        return None
    if ref < 0:
        return None  # palette in another package; unsupported for now
    e = pkg.exports[ref - 1]
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    read_properties(r)
    n = r.compact()
    if n <= 0 or n > 256 or n * 4 > r.remaining():
        return None
    raw = np.frombuffer(r.raw(n * 4), dtype=np.uint8).reshape(n, 4)
    pal = np.zeros((256, 4), np.uint8)
    pal[:n] = raw  # stored as RGBA in UE2 FColor order B,G,R,A -> swap
    pal[:n] = raw[:, [2, 1, 0, 3]]
    return pal


def read_texture(pkg: Package, e: Export, mip: int = 0) -> Texture:
    r = pkg.export_reader(e)
    skip_state_frame(r, e.flags)
    props = read_properties(r)
    fmt = int(props.get("Format", TEXF_P8) or 0)
    usize = int(props.get("USize", 0) or 0)
    vsize = int(props.get("VSize", 0) or 0)
    mip_count = r.compact()
    if mip_count < 0 or mip_count > 32:
        raise PackageError(f"{e.name}: bad mip count {mip_count}")
    chosen = None
    for i in range(mip_count):
        r.i32()  # lazy skip
        size = r.compact()
        if size < 0 or size > r.remaining():
            raise PackageError(f"{e.name}: bad mip size {size}")
        data = r.raw(size)
        w = r.i32()
        h = r.i32()
        r.u8()
        r.u8()
        if i == mip:
            chosen = (data, w, h)
        if chosen is not None:
            break
    if chosen is None:
        return Texture(e.name, usize, vsize, fmt, None, props)
    data, w, h = chosen
    if w <= 0 or h <= 0:
        w, h = usize, vsize
    image = None
    has_alpha = False
    try:
        if fmt in _DDS_FOURCC:
            image = Image.open(io.BytesIO(_dds_bytes(fmt, w, h, data))).convert("RGBA")
            has_alpha = fmt != TEXF_DXT1 or bool(props.get("bAlphaTexture"))
        elif fmt == TEXF_RGBA8:
            arr = np.frombuffer(data[:w * h * 4], dtype=np.uint8).reshape(h, w, 4)[:, :, [2, 1, 0, 3]]
            image = Image.fromarray(np.ascontiguousarray(arr), "RGBA")
            has_alpha = bool(props.get("bAlphaTexture"))
        elif fmt == TEXF_P8:
            pal = _read_palette(pkg, int(props.get("Palette", 0) or 0))
            idx = np.frombuffer(data[:w * h], dtype=np.uint8).reshape(h, w)
            if pal is None:
                gray = np.stack([idx, idx, idx, np.full_like(idx, 255)], axis=-1)
                image = Image.fromarray(gray, "RGBA")
            else:
                image = Image.fromarray(pal[idx], "RGBA")
        elif fmt == TEXF_L8:
            l = np.frombuffer(data[:w * h], dtype=np.uint8).reshape(h, w)
            image = Image.fromarray(np.stack([l, l, l, np.full_like(l, 255)], axis=-1), "RGBA")
        elif fmt == TEXF_RGB8:
            arr = np.frombuffer(data[:w * h * 3], dtype=np.uint8).reshape(h, w, 3)[:, :, ::-1]
            image = Image.fromarray(np.ascontiguousarray(arr), "RGB").convert("RGBA")
    except Exception as ex:  # decoding failure is not fatal for the pipeline
        image = None
    return Texture(e.name, w, h, fmt, image, props, has_alpha)
