"""Hub art: every texture of the hub map, drawn here from code. An OpenSCCT addition, not a port.

Nothing is read from the game or from any other art: tiles are numpy patterns with a seeded random generator, signs
are drawn with JetBrains Mono Bold (SIL Open Font License 1.1, game/branding/fonts/jetbrains-mono/OFL.txt), the font
the menus already ship. The same input gives the same PNG bytes on every run (no clock, seeded noise).

`texture(kind)` returns the PNG of a tile or panel kind; `sign(...)` draws a text panel. tools/hubmap/generate.py
calls both while it writes the render folder.
"""

from __future__ import annotations

import io
import math
import zlib
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO = Path(__file__).resolve().parents[2]
FONT = REPO / "game" / "branding" / "fonts" / "jetbrains-mono" / "JetBrainsMono-Bold.ttf"

# The palette: OpenSCCT's night-vision green (the icon and splash), a merc amber, and the night.
GREEN = (120, 255, 140)
AMBER = (255, 168, 60)
CYAN = (90, 220, 255)
MAGENTA = (255, 80, 200)
WHITE = (235, 240, 245)
NIGHT = (8, 11, 20)

NEON = {
    "neon_green": GREEN, "neon_amber": AMBER, "neon_cyan": CYAN, "neon_magenta": MAGENTA, "neon_white": WHITE,
    "neon_ice": (170, 225, 255), "neon_sodium": (255, 150, 40), "neon_teal": (40, 240, 200), "neon_lab": (150, 255, 60),
    "neon_gold": (255, 210, 110), "neon_red": (255, 60, 50),
}


def _png(img: Image.Image) -> bytes:
    out = io.BytesIO()
    img.save(out, "PNG", optimize=True)
    return out.getvalue()


def _rng(kind: str) -> np.random.Generator:
    return np.random.default_rng(zlib.crc32(kind.encode()))


def _noise(rng, size, scale, octaves=3) -> np.ndarray:
    """Tileable value noise in 0..1 (sums of upsampled random grids, wrapped)."""
    acc = np.zeros((size, size))
    amp, total = 1.0, 0.0
    cells = scale
    for _ in range(octaves):
        g = rng.random((cells, cells))
        rep = size // cells
        up = np.kron(g, np.ones((rep, rep)))
        # Smooth the blocks by averaging with shifted copies (wrapped, so the tile stays seamless).
        for _k in range(2):
            up = (up + np.roll(up, rep // 2, 0) + np.roll(up, rep // 2, 1) + np.roll(np.roll(up, rep // 2, 0), rep // 2, 1)) / 4
        acc += amp * up
        total += amp
        amp *= 0.5
        cells = min(size, cells * 2)
    return acc / total


def _img(arr) -> Image.Image:
    return Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8), "RGB")


def _tint(base, shade: np.ndarray) -> np.ndarray:
    return np.asarray(base, dtype=np.float64)[None, None, :] * shade[..., None]


def _quantise(arr, step=4):
    return (np.round(np.asarray(arr) / step) * step)


# ---- tiles (world UVs) ---------------------------------------------------------------------------------------------

def _concrete(kind, base, size=128, seams=True, spread=0.18):
    rng = _rng(kind)
    n = _noise(rng, size, 8)
    shade = 1.0 - spread / 2 + spread * n
    speck = rng.random((size, size)) < 0.015
    shade = np.where(speck, shade * 0.8, shade)
    arr = _tint(base, shade)
    if seams:
        yy, xx = np.mgrid[0:size, 0:size]
        line = (xx < 1) | (yy < 1)
        arr = np.where(line[..., None], arr * 0.6, arr)
    return _quantise(arr)


def _planks(kind, base, size=128, boards=8, vertical=False):
    rng = _rng(kind)
    yy, xx = np.mgrid[0:size, 0:size]
    if vertical:
        xx, yy = yy, xx
    board = yy * boards // size
    grain = 0.9 + 0.1 * np.sin(xx / 3.0 + board * 1.7 + 2 * np.sin(yy / 5.0))
    tone = (0.85 + 0.25 * rng.random(boards))[board]
    shade = grain * tone
    gap = (yy % (size // boards)) == 0
    shade = np.where(gap, 0.35, shade)
    return _quantise(_tint(base, shade))


def _diamond_plate(kind, base, size=128):
    yy, xx = np.mgrid[0:size, 0:size]
    a = ((xx + yy) % 16 < 3) & ((xx // 16 + yy // 16) % 2 == 0)
    b = ((xx - yy) % 16 < 3) & ((xx // 16 + yy // 16) % 2 == 1)
    shade = np.where(a | b, 1.25, 0.9)
    edge = (xx < 1) | (yy < 1)
    shade = np.where(edge, 0.6, shade)
    return _quantise(_tint(base, shade))


def _corrugated(kind, base, size=128):
    rng = _rng(kind)
    yy, xx = np.mgrid[0:size, 0:size]
    shade = 0.8 + 0.25 * np.sin(xx * 2 * math.pi / 16.0)
    rust = _noise(rng, size, 4) > 0.62
    arr = _tint(base, shade)
    arr = np.where(rust[..., None], arr * np.array([1.1, 0.75, 0.55]), arr)
    return _quantise(arr)


def _leaves(kind, base, size=128):
    rng = _rng(kind)
    n = _noise(rng, size, 16, octaves=2)
    blobs = rng.random((size, size))
    shade = 0.55 + 0.6 * n + 0.25 * (blobs > 0.8)
    return _quantise(_tint(base, shade), 6)


def _fabric(kind, base, size=64):
    yy, xx = np.mgrid[0:size, 0:size]
    weave = 0.9 + 0.1 * (((xx // 2) + (yy // 2)) % 2)
    seam = (xx % 32 == 0) | (yy % 32 == 0)
    return _quantise(_tint(base, np.where(seam, 0.7, weave)))


def _carpet(kind, size=128):
    yy, xx = np.mgrid[0:size, 0:size]
    base = np.array([70, 18, 34], dtype=np.float64)
    d = (np.abs((xx % 32) - 16) + np.abs((yy % 32) - 16))
    arr = np.tile(base, (size, size, 1))
    arr = np.where((d == 12)[..., None], np.array([170, 120, 50]), arr)
    arr = np.where((d < 3)[..., None], np.array([120, 40, 60]), arr)
    return arr


def _turf(kind, size=128):
    rng = _rng(kind)
    yy, xx = np.mgrid[0:size, 0:size]
    stripe = np.where((xx // 64) % 2 == 0, 1.0, 0.86)
    blades = 0.88 + 0.24 * (rng.random((size, size)) > 0.5)
    return _quantise(_tint((44, 98, 50), stripe * blades), 6)


def _pavers(kind, size=128):
    """Granite pavers, two by two per tile, with a thin green inlay along one joint."""
    rng = _rng(kind)
    yy, xx = np.mgrid[0:size, 0:size]
    cell = (xx // 64) + 2 * (yy // 64)
    tone = (0.9 + 0.18 * rng.random(4))[cell]
    n = _noise(rng, size, 16, octaves=2)
    arr = _tint((88, 88, 94), tone * (0.85 + 0.3 * n))
    joint = (xx % 64 < 2) | (yy % 64 < 2)
    arr = np.where(joint[..., None], np.array([30, 32, 36]), arr)
    inlay = (xx < 2) & (yy % 64 > 8) & (yy % 64 < 56)
    arr = np.where(inlay[..., None], np.array([60, 140, 80]), arr)
    return _quantise(arr)


def _tiles(kind, base, size=128, n=4, grout=(60, 64, 70)):
    yy, xx = np.mgrid[0:size, 0:size]
    rng = _rng(kind)
    step = size // n
    tone = 0.94 + 0.08 * rng.random((n, n))
    arr = _tint(base, tone[yy // step, xx // step])
    joint = (xx % step < 2) | (yy % step < 2)
    return np.where(joint[..., None], np.array(grout), arr)


def _checker(kind, a, b, size=128, n=2):
    yy, xx = np.mgrid[0:size, 0:size]
    rng = _rng(kind)
    vein = _noise(rng, size, 8, octaves=3)
    vein = 0.9 + 0.15 * np.abs(np.sin(vein * 18))
    m = ((xx // (size // n) + yy // (size // n)) % 2 == 0)
    arr = np.where(m[..., None], np.array(a, dtype=np.float64), np.array(b, dtype=np.float64)) * vein[..., None]
    return _quantise(arr)


def _crate(kind, base, size=128):
    """A crate face: planks inside a frame with a cross brace."""
    arr = _planks(kind, base, size, boards=6)
    yy, xx = np.mgrid[0:size, 0:size]
    frame = (xx < 12) | (xx >= size - 12) | (yy < 12) | (yy >= size - 12)
    brace = np.abs(xx - yy) < 8
    dark = np.asarray(base, dtype=np.float64) * 0.7
    arr = np.where((frame | brace)[..., None], dark, arr)
    edge = (xx < 2) | (yy < 2) | (xx >= size - 2) | (yy >= size - 2)
    return np.where(edge[..., None], dark * 0.5, arr)


def _hazard(kind, a, b, size=64):
    yy, xx = np.mgrid[0:size, 0:size]
    m = ((xx + yy) // 16) % 2 == 0
    return np.where(m[..., None], np.array(a), np.array(b))


def _panel(kind, base, size=128, accent=None):
    """Wall panels: a 2 by 1 grid of plates with bolts; an optional accent stripe."""
    arr = _concrete(kind, base, size, seams=False, spread=0.1)
    yy, xx = np.mgrid[0:size, 0:size]
    seam = (xx % 64 < 2) | (yy % 128 < 2)
    arr = np.where(seam[..., None], arr * 0.55, arr)
    bolt = ((xx % 64 - 8) ** 2 + (yy % 64 - 8) ** 2) < 5
    arr = np.where(bolt[..., None], arr * 1.3, arr)
    if accent is not None:
        stripe = (yy > 96) & (yy < 104)
        arr = np.where(stripe[..., None], np.array(accent), arr)
    return arr


def _stars(kind, size=256):
    rng = _rng(kind)
    yy, xx = np.mgrid[0:size, 0:size]
    n = _noise(rng, size, 4, octaves=2)
    arr = _tint((12, 16, 34), 0.8 + 0.4 * n)
    stars = rng.random((size, size))
    bright = stars > 0.9965
    arr = np.where(bright[..., None], np.array([220, 225, 255]) * (0.6 + 0.4 * rng.random((size, size)))[..., None], arr)
    return _quantise(arr, 2)


def _skyline(kind, w=1024, h=256):
    """The backdrop on the hall's walls: a parapet, then a night city of our own towers with lit windows, then sky.

    Mapped 4096 uu wide and the hall's 1536 high: row 0 is the ceiling, the last row the floor.
    """
    rng = _rng(kind)
    rows = np.arange(h)[:, None]
    t = rows / (h - 1)                     # 0 top .. 1 floor
    sky = np.array([10, 13, 32]) * (1 - t[..., None]) + np.array([60, 30, 70]) * t[..., None]
    arr = np.tile(sky, (1, w, 1)).astype(np.float64)
    horizon = int(h * 0.86)
    # Two layers of towers: far ones dim and tall, near ones darker and lower.
    for layer, (dim, top_lo, top_hi, wmin, wmax) in enumerate(((0.55, 0.25, 0.6, 24, 64), (0.3, 0.45, 0.75, 32, 96))):
        x = 0
        while x < w:
            bw = int(rng.integers(wmin, wmax))
            top = int(h * rng.uniform(top_lo, top_hi))
            colour = np.array([18, 22, 38]) * dim * (1.6 if layer == 0 else 1.0)
            x1 = min(w, x + bw)
            arr[top:horizon, x:x1] = colour
            # Windows: a grid, some lit warm, a few cool.
            for wy in range(top + 4, horizon - 2, 6):
                for wx in range(x + 3, x1 - 2, 5):
                    r = rng.random()
                    if r < (0.18 if layer == 1 else 0.1):
                        lit = np.array([255, 205, 120]) if rng.random() < 0.8 else np.array([140, 220, 255])
                        arr[wy:wy + 2, wx:wx + 2] = lit * (0.5 + 0.5 * rng.random()) * (0.7 if layer == 0 else 1.0)
            if rng.random() < 0.25 and top > 8:
                arr[top - 6:top, x + bw // 2:x + bw // 2 + 1] = np.array([90, 90, 100])
                arr[top - 7:top - 5, x + bw // 2 - 1:x + bw // 2 + 2] = np.array([255, 60, 50])
            x = x1 + int(rng.integers(0, 6))
    # The parapet: concrete with a green neon line along its top.
    para = _concrete(kind + "p", (34, 36, 42), 64, seams=False)
    for x in range(0, w, 32):
        arr[horizon:h, x:x + 32] = para[: h - horizon, :32]
    arr[horizon - 3:horizon, :] = np.array(GREEN) * 0.95
    arr[horizon:horizon + 2, :] = np.array(GREEN) * 0.4
    return _quantise(arr, 2)


def _neon(colour, size=16):
    yy = np.arange(size)[:, None] / (size - 1)
    core = 1.0 - 0.35 * np.abs(yy - 0.5) * 2
    arr = np.asarray(colour, dtype=np.float64)[None, None, :] * core[..., None]
    arr = arr + 255 * np.clip(0.5 - np.abs(yy - 0.5) * 2, 0, 1)[..., None] * 0.6
    return np.tile(arr, (1, size, 1))


def _screen(kind, size=64):
    yy, xx = np.mgrid[0:size, 0:size]
    arr = np.tile(np.array([10, 22, 34], dtype=np.float64), (size, size, 1))
    scan = (yy % 4 == 0)
    return np.where(scan[..., None], arr * 1.6, arr)


def _ice(kind, size=128):
    rng = _rng(kind)
    n = _noise(rng, size, 8)
    arr = _tint((200, 225, 245), 0.85 + 0.2 * n)
    yy, xx = np.mgrid[0:size, 0:size]
    crack = (np.abs(np.sin((xx * 0.7 + yy * 0.3) / 9 + n * 6)) < 0.04)
    return _quantise(np.where(crack[..., None], np.array([150, 190, 225]), arr))


def _frost_metal(kind, size=64):
    rng = _rng(kind)
    n = _noise(rng, size, 8)
    return _quantise(_tint((150, 175, 200), 0.8 + 0.35 * n))


def _grating(kind, base, size=64):
    yy, xx = np.mgrid[0:size, 0:size]
    bars = (xx % 8 < 2) | (yy % 16 < 2)
    return np.where(bars[..., None], np.array(base, dtype=np.float64), np.array(base, dtype=np.float64) * 0.25)


# Kind -> (drawer). Tiles stay small (64 or 128 px) so the folder stays light.
TILES = {
    "roof": lambda k: _concrete(k, (46, 48, 54)),
    "concrete": lambda k: _concrete(k, (98, 98, 102)),
    "darkpanel": lambda k: _panel(k, (54, 58, 66)),
    "plaza": _pavers,
    "turf": _turf,
    "carpet": _carpet,
    "court": lambda k: _concrete(k, (34, 40, 52), seams=False, spread=0.1),
    "track": lambda k: _concrete(k, (24, 60, 72), seams=False, spread=0.12),
    "range": lambda k: _concrete(k, (70, 70, 66), spread=0.14),
    "booth": lambda k: _planks(k, (110, 76, 46)),
    "wood": lambda k: _planks(k, (138, 98, 58)),
    "metal": lambda k: _concrete(k, (70, 74, 82), seams=False, spread=0.08),
    "deck": lambda k: _diamond_plate(k, (92, 98, 108)),
    "fence": lambda k: np.where((((np.mgrid[0:128, 0:128][0] + np.mgrid[0:128, 0:128][1]) % 16 < 2)
                                 | ((np.mgrid[0:128, 0:128][0] - np.mgrid[0:128, 0:128][1]) % 16 < 2))[..., None],
                                np.array([150, 160, 170]), np.array([18, 20, 24])),
    "hedge": lambda k: _leaves(k, (40, 96, 46)),
    "foliage": lambda k: _leaves(k, (30, 80, 52)),
    "bark": lambda k: _planks(k, (70, 50, 34), boards=4, vertical=True),
    "sofa": lambda k: _fabric(k, (28, 84, 92)),
    "sofa2": lambda k: _fabric(k, (96, 40, 84)),
    "crate": lambda k: _crate(k, (132, 96, 58)),
    "hvac": lambda k: _grating(k, (120, 126, 132)),
    "sky": _stars,
    "screen": _screen,
    # Theme kits (match pads): one look per theme.
    "hub_slate": lambda k: _panel(k, (48, 54, 64), accent=GREEN),
    "polar_ice": _ice,
    "polar_frost": _frost_metal,
    "steel_corrugated": lambda k: _corrugated(k, (110, 104, 96)),
    "steel_concrete": lambda k: _concrete(k, (112, 108, 100)),
    "aqua_metal": lambda k: _panel(k, (36, 96, 104), accent=(40, 240, 200)),
    "aqua_grating": lambda k: _grating(k, (60, 150, 150)),
    "lab_panel": lambda k: _tiles(k, (220, 226, 228), n=2, grout=(150, 160, 160)),
    "lab_hazard": lambda k: _hazard(k, (240, 200, 30), (20, 20, 20)),
    "museum_marble": lambda k: _checker(k, (220, 214, 200), (60, 52, 48)),
    "museum_wood": lambda k: _planks(k, (100, 52, 30), boards=4),
}
TILES["water"] = lambda k: _quantise(_tint((30, 120, 140), 0.75 + 0.35 * _noise(_rng(k), 128, 8, octaves=2)), 4)
TILES.update({k: (lambda c: (lambda _k: _neon(c)))(c) for k, c in NEON.items()})
TILES["skyline"] = _skyline


def texture(kind: str) -> bytes:
    if kind not in TILES:
        raise KeyError(f"no hub texture kind {kind}")
    return _png(_img(TILES[kind](kind)))


# ---- signs and decals (their own UVs) ------------------------------------------------------------------------------

def _font(px: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(str(FONT), px)


def _fit(draw, text, max_w, max_h, start):
    px = start
    while px > 8:
        f = _font(px)
        l, t, r, b = draw.textbbox((0, 0), text, font=f)
        if r - l <= max_w and b - t <= max_h:
            return f, (l, t, r, b)
        px -= 2
    f = _font(8)
    return f, draw.textbbox((0, 0), text, font=f)


def sign(lines: list[str], w: int, h: int, fg=GREEN, bg=NIGHT, border=True, glow=True, sub_fg=None,
         transparent_bg=False) -> bytes:
    """A neon sign panel: the first line big, the others smaller under it, with a soft glow and a frame."""
    img = Image.new("RGB", (w, h), bg)
    text = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(text)
    pad = max(4, h // 12)
    big = h * (0.62 if len(lines) > 1 else 0.8)
    f, (l, t, r, b) = _fit(d, lines[0], w - 2 * pad, big - pad, int(big))
    y = pad + (big - pad - (b - t)) / 2 - t if len(lines) > 1 else (h - (b - t)) / 2 - t
    d.text(((w - (r - l)) / 2 - l, y), lines[0], fill=255, font=f)
    sub_mask = Image.new("L", (w, h), 0)
    if len(lines) > 1:
        ds = ImageDraw.Draw(sub_mask)
        rest = "  ".join(lines[1:])
        fs, (l2, t2, r2, b2) = _fit(ds, rest, w - 2 * pad, h - big - pad, int((h - big) * 0.7))
        ds.text(((w - (r2 - l2)) / 2 - l2, big + (h - big - pad - (b2 - t2)) / 2 - t2), rest, fill=255, font=fs)
    if border:
        ImageDraw.Draw(text).rectangle([2, 2, w - 3, h - 3], outline=200, width=max(2, h // 40))
    colour = Image.new("RGB", (w, h), fg)
    if glow:
        halo = text.filter(ImageFilter.GaussianBlur(max(2, h // 16)))
        img = Image.composite(Image.blend(img, colour, 0.55), img, halo)
    img = Image.composite(colour, img, text)
    core = text.point(lambda v: 255 if v > 200 else 0).filter(ImageFilter.GaussianBlur(0.6))
    img = Image.composite(Image.blend(colour, Image.new("RGB", (w, h), (255, 255, 255)), 0.55), img, core)
    if len(lines) > 1:
        img = Image.composite(Image.new("RGB", (w, h), sub_fg or WHITE), img, sub_mask)
    return _png(img)


def floor_label(text: str, w: int = 512, h: int = 128, fg=WHITE, bg=None, arrow: bool = False) -> bytes:
    """Floor paint: bold letters (and an arrow pointing up the image) on the floor colour."""
    img = Image.new("RGB", (w, h), bg or (40, 42, 48))
    d = ImageDraw.Draw(img)
    pad = h // 8
    aw = h if arrow else 0
    f, (l, t, r, b) = _fit(d, text, w - 2 * pad - aw, h - 2 * pad, int(h * 0.7))
    d.text((aw + (w - aw - (r - l)) / 2 - l, (h - (b - t)) / 2 - t), text, fill=fg, font=f)
    if arrow:
        cx, cy = pad + (aw - pad) / 2, h / 2
        s = (h - 2 * pad) / 2
        d.polygon([(cx, cy - s), (cx + s, cy), (cx + s * 0.4, cy), (cx + s * 0.4, cy + s), (cx - s * 0.4, cy + s),
                   (cx - s * 0.4, cy), (cx - s, cy)], fill=fg)
    return _png(img)


def plaza_mark(size: int = 512) -> bytes:
    """The plaza's floor emblem: the OpenSCCT ring (a ring with a scan gap and four ticks), inlaid in the pavers."""
    s = size * 2
    img = Image.new("RGB", (s, s), (70, 72, 78))
    d = ImageDraw.Draw(img)
    c = s / 2
    for r_out, r_in, col in ((0.49, 0.47, (60, 140, 80)), (0.44, 0.33, (110, 230, 130)), (0.31, 0.30, (60, 140, 80))):
        d.ellipse([c - s * r_out, c - s * r_out, c + s * r_out, c + s * r_out], fill=col)
        d.ellipse([c - s * r_in, c - s * r_in, c + s * r_in, c + s * r_in], fill=(70, 72, 78))
    gap = s * 0.03
    d.rectangle([0, c - gap, s, c + gap], fill=(70, 72, 78))
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        x0, y0 = c + dx * s * 0.455, c + dy * s * 0.455
        w = s * 0.012
        d.rectangle([x0 - w - abs(dy) * 0 - (s * 0.03 if dx else 0), y0 - w - (s * 0.03 if dy else 0),
                     x0 + w + (s * 0.03 if dx else 0), y0 + w + (s * 0.03 if dy else 0)], fill=(110, 230, 130))
    return _png(img.resize((size, size), Image.Resampling.LANCZOS))


def target_board(size: int = 128) -> bytes:
    """A painted target on the range's berm: rings on a board."""
    img = Image.new("RGB", (size, size * 2), (60, 50, 40))
    d = ImageDraw.Draw(img)
    c = (size / 2, size)
    for k, col in enumerate(((235, 235, 225), (30, 30, 30), (235, 235, 225), (220, 60, 40))):
        r = size * (0.45 - 0.11 * k)
        d.ellipse([c[0] - r, c[1] - r, c[0] + r, c[1] + r], fill=col)
    d.rectangle([size / 2 - 1, 8, size / 2 + 1, size * 2 - 8], fill=(220, 60, 40))
    return _png(img)


def checker_strip(w: int = 256, h: int = 64, label: str = "") -> bytes:
    img = Image.new("RGB", (w, h), (235, 235, 235))
    d = ImageDraw.Draw(img)
    q = h // 4
    for i in range(0, w, q):
        for j in range(0, h, q):
            if (i // q + j // q) % 2 == 0:
                d.rectangle([i, j, i + q - 1, j + q - 1], fill=(20, 20, 20))
    if label:
        f, (l, t, r, b) = _fit(d, label, w - 16, h - 16, h)
        d.rectangle([(w - (r - l)) / 2 - 8, (h - (b - t)) / 2 - 4, (w + (r - l)) / 2 + 8, (h + (b - t)) / 2 + 4],
                    fill=(20, 20, 20))
        d.text(((w - (r - l)) / 2 - l, (h - (b - t)) / 2 - t), label, fill=GREEN, font=f)
    return _png(img)


def court(theme: str, line, floor, accent, size: int = 512) -> bytes:
    """A match pad court: a border, a centre circle, a half line and corner marks, in a theme's colours."""
    rng = _rng("court" + theme)
    base = np.tile(np.asarray(floor, dtype=np.float64), (size, size, 1))
    n = _noise(rng, size, 8, octaves=2)
    base = _quantise(base * (0.9 + 0.12 * n[..., None]), 4)
    img = _img(base)
    d = ImageDraw.Draw(img)
    lw = max(3, size // 96)
    m = size // 24
    d.rectangle([m, m, size - m, size - m], outline=line, width=lw)
    d.rectangle([m * 2, m * 2, size - m * 2, size - m * 2], outline=accent, width=max(1, lw // 2))
    d.line([m, size / 2, size - m, size / 2], fill=line, width=lw)
    for r, col, wd in ((size * 0.16, line, lw), (size * 0.05, accent, 0)):
        if wd:
            d.ellipse([size / 2 - r, size / 2 - r, size / 2 + r, size / 2 + r], outline=col, width=wd)
        else:
            d.ellipse([size / 2 - r, size / 2 - r, size / 2 + r, size / 2 + r], fill=col)
    for cx, cy in ((m * 3, m * 3), (size - m * 3, m * 3), (m * 3, size - m * 3), (size - m * 3, size - m * 3)):
        d.rectangle([cx - m, cy - lw, cx + m, cy + lw], fill=accent)
        d.rectangle([cx - lw, cy - m, cx + lw, cy + m], fill=accent)
    return _png(img)


def board(title: str, sub: str, w: int = 512, h: int = 512) -> bytes:
    """A screen board: a header strip with the title, a dark screen under it with faint rows (left for live text)."""
    img = Image.new("RGB", (w, h), (10, 22, 34))
    d = ImageDraw.Draw(img)
    head = h // 5
    d.rectangle([0, 0, w - 1, head], fill=(16, 34, 26))
    f, (l, t, r, b) = _fit(d, title, w - 24, head * 0.62, int(head * 0.62))
    d.text(((w - (r - l)) / 2 - l, head * 0.08 - t + (head * 0.62 - (b - t)) / 2), title, fill=GREEN, font=f)
    fs, (l2, t2, r2, b2) = _fit(d, sub.upper(), w - 24, head * 0.26, int(head * 0.26))
    d.text(((w - (r2 - l2)) / 2 - l2, head * 0.7 - t2), sub.upper(), fill=WHITE, font=fs)
    for y in range(head + 8, h, 4):
        d.line([0, y, w, y], fill=(14, 30, 44))
    rows = 6
    for k in range(rows):
        y = head + 16 + k * (h - head - 32) // rows
        d.rectangle([16, y, w - 16, y + (h - head - 32) // rows - 8], outline=(24, 52, 70), width=2)
    d.rectangle([0, 0, w - 1, h - 1], outline=GREEN, width=4)
    return _png(img)


def ticker(text: str, w: int = 2048, h: int = 64) -> bytes:
    """A news ticker strip (it scrolls with a panner): amber letters on black. The strip wraps, so it holds a whole
    number of copies of the text with the spare width shared out between them; a copy cut at the right edge would
    run into the start of the next pass. The font shrinks only when one copy is wider than the strip."""
    img = Image.new("RGB", (w, h), (6, 6, 8))
    d = ImageDraw.Draw(img)
    px = int(h * 0.62)
    f = _font(px)
    while px > 8 and f.getlength(text) > w:
        px -= 1
        f = _font(px)
    advance = f.getlength(text)    # with the trailing spaces, which textbbox leaves out
    copies = max(1, int(w // advance))
    step = w / copies
    l, t, r, b = d.textbbox((0, 0), text, font=f)
    for k in range(copies):
        d.text((round(k * step) - l, (h - (b - t)) / 2 - t), text, fill=AMBER, font=f)
    return _png(img)


def helipad(size: int = 512) -> bytes:
    """A rooftop helipad: a ring and a big H, painted on the deck."""
    s = size
    img = Image.new("RGB", (s, s), (46, 48, 54))
    d = ImageDraw.Draw(img)
    c = s / 2
    d.ellipse([c - s * 0.46, c - s * 0.46, c + s * 0.46, c + s * 0.46], fill=(220, 200, 60))
    d.ellipse([c - s * 0.42, c - s * 0.42, c + s * 0.42, c + s * 0.42], fill=(46, 48, 54))
    bw, bh = s * 0.07, s * 0.44
    for dx in (-s * 0.14, s * 0.14):
        d.rectangle([c + dx - bw / 2, c - bh / 2, c + dx + bw / 2, c + bh / 2], fill=(235, 235, 225))
    d.rectangle([c - s * 0.14, c - bw / 2, c + s * 0.14, c + bw / 2], fill=(235, 235, 225))
    return _png(img)
