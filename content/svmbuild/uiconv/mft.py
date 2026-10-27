"""Magma Font (.mft) reader: the glyph table of a Magma bitmap font.

Layout (Menus/Fonts/*.mft):

    "Magma Font\\0"          10 chars and a NUL
    int32 version           (0x82 in the shipped files)
    font name, NUL-terminated Latin-1, then 0xCD padding
    int32 size              nominal pixel height; Magma text heights scale glyphs by height / size
    three bytes             (00 e0 00 in the shipped files; meaning unknown)
    23-byte glyph entries   uint16 char, uint8 width, uint8 height, int8 x offset, int8 baseline (rows above the
                            pen line; negative for glyphs below it, such as the underscore), uint8 advance, then
                            float u0, v0, u1, v1 whose integer part is the atlas page and whose fraction is the UV
                            inside that page

Entries are in increasing character order; the table ends at the first entry that breaks the order. The atlas pages
are textures named "<font name with _ for spaces>_<page>" in the Magma texture packages, stored bottom-up: the pipeline
writes them flipped so rows read top-down, and the glyph v is measured from the top of the flipped page.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass, field

MAGIC = b"Magma Font\x00"
ENTRY_SIZE = 23
PRE_TABLE_BYTES = 3
MAX_CHAR = 0x2FFF


class MftError(ValueError):
    pass


@dataclass
class Glyph:
    char: int
    width: int
    height: int
    xoff: int
    base: int
    advance: int
    page: int
    u0: float
    v0: float
    u1: float
    v1: float


@dataclass
class MagmaFont:
    name: str
    version: int
    size: int
    glyphs: dict[int, Glyph] = field(default_factory=dict)

    @property
    def pages(self) -> int:
        return 1 + max((g.page for g in self.glyphs.values()), default=-1)


def _frac(x: float) -> float:
    return x - int(x)


def parse_mft(data: bytes) -> MagmaFont:
    if not data.startswith(MAGIC):
        raise MftError("not a Magma Font file")
    p = len(MAGIC)
    if p + 4 > len(data):
        raise MftError("truncated header")
    version = struct.unpack_from("<i", data, p)[0]
    p += 4
    end = data.find(b"\x00", p)
    if end < 0:
        raise MftError("font name is not terminated")
    name = data[p:end].decode("latin-1")
    p = end + 1
    while p < len(data) and data[p] == 0xCD:
        p += 1
    if p + 4 + PRE_TABLE_BYTES > len(data):
        raise MftError("truncated before the glyph table")
    size = struct.unpack_from("<i", data, p)[0]
    p += 4 + PRE_TABLE_BYTES
    font = MagmaFont(name, version, size)
    last = -1
    while p + ENTRY_SIZE <= len(data):
        ch = struct.unpack_from("<H", data, p)[0]
        if ch <= last or ch > MAX_CHAR:
            break
        w, h = data[p + 2], data[p + 3]
        xoff, base = struct.unpack_from("<bb", data, p + 4)
        adv = data[p + 6]
        u0, v0, u1, v1 = struct.unpack_from("<4f", data, p + 7)
        font.glyphs[ch] = Glyph(ch, w, h, xoff, base, adv, int(u0), _frac(u0), _frac(v0), _frac(u1), _frac(v1))
        last = ch
        p += ENTRY_SIZE
    if not font.glyphs:
        raise MftError(f"{name}: empty glyph table")
    return font


def texture_prefix(font_name: str) -> str:
    """Atlas page texture names: "HandelGothic BT Regular 20" -> "HandelGothic_BT_Regular_20_" + page."""
    return font_name.replace(" ", "_") + "_"


def font_key(font_name: str) -> str:
    return font_name.replace(" ", "_")


GLYPH_PADDING = 2
REPACK_WIDTH = 512


def repack_glyphs(pages: list, glyphs: list[dict], padding: int = GLYPH_PADDING, width: int = REPACK_WIDTH):
    """Copy every glyph cell into a new atlas with `padding` transparent pixels round each one.

    The game's pages pack glyph cells edge to edge, so a scaled, filtered draw of one cell picks up its neighbours;
    an inset source rect avoids that but drops the glyph's edge rows at small scales. With the gap the client can
    sample the exact cell. Shelf packing, tallest first, ties by character code: the result is deterministic.
    Returns (new page images, new glyph table); glyphs without pixels keep zero rects.
    """
    from PIL import Image  # noqa: PLC0415

    cells = [g for g in glyphs if g["w"] > 0 and g["h"] > 0]
    order = sorted(cells, key=lambda g: (-g["h"], g["c"]))
    placed: dict[int, tuple[int, int]] = {}
    x = y = row = 0
    for g in order:
        cw, ch = g["w"] + 2 * padding, g["h"] + 2 * padding
        if cw > width:
            raise ValueError(f"glyph {g['c']} is wider than the repacked page ({cw} > {width})")
        if x + cw > width:
            x, y, row = 0, y + row, 0
        placed[g["c"]] = (x + padding, y + padding)
        x += cw
        row = max(row, ch)
    height = max(4, (y + row + 3) // 4 * 4)
    page = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for g in order:
        cell = pages[g["page"]].crop((g["x"], g["y"], g["x"] + g["w"], g["y"] + g["h"]))
        page.paste(cell, placed[g["c"]])
    out = []
    for g in glyphs:
        n = dict(g)
        if g["c"] in placed:
            n["x"], n["y"] = placed[g["c"]]
            n["page"] = 0
        else:
            n["x"] = n["y"] = n["page"] = 0
        out.append(n)
    return [page], out


def glyph_table(font: MagmaFont, page_sizes: list[tuple[int, int]]) -> list[dict]:
    """Glyphs in pixel units of their (flipped) page, for the client: one entry per character in the table."""
    out = []
    for ch in sorted(font.glyphs):
        g = font.glyphs[ch]
        if g.page >= len(page_sizes):
            continue
        pw, ph = page_sizes[g.page]
        out.append({"c": ch, "x": int(round(g.u0 * pw)), "y": int(round(g.v0 * ph)), "w": g.width, "h": g.height,
                    "page": g.page, "xoff": g.xoff, "base": g.base, "adv": g.advance})
    return out
