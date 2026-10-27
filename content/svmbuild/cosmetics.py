"""OpenSCCT's own cosmetic camos (not retail): each pattern is painted over the player's own retail character and gun
textures, keeping their shading, as shared/chars/<Package>/textures/cosmetic_<pattern>_<texture>.png. The game's
SkeletalModel.SetCosmetic draws them (src/OpenScct.Net/Cosmetics/CosmeticCatalog.cs lists the items).

Sizes: a camo is written at four times its retail texture's size (bodies 2048 px, the second spy suit texture 1024,
guns and the pistol 1024, the smaller gun parts in step but never under 64 px); the map camos on bodies stop at 1024 to
save disk. The retail texture is enlarged first (Lanczos, alpha too) and the pattern painted at full size. The game
takes any size, as UVs are normalised.

Every shape is measured in texture units (the long side is 1), never in pixels, and its noise is seeded per pattern and
texture, so a camo is the same design at any size, only crisper. Hard edges are anti-aliased over about one pixel.
Small motifs (cogs, bolts, rivets, chevrons, circuit traces, lightning, shards, ...) are scattered and stamped one by
one into their own small tile, wrapping across the texture edges: the textures are UV atlases of many small islands,
so motifs stay small (a few percent of the texture) rather than one big drawing.

Each pattern also gets a flat 256 px swatch, shared/chars/thumbs/cosmetic_<pattern>.png, for the menus (a painted
atlas reads as noise when shown flat).

The pattern table below must match CosmeticCatalog.Patterns and CosmeticCatalog.Maps (ids and colours); the shapes are
drawn here procedurally with fixed seeds, so every install builds the same camos.
"""

from __future__ import annotations

import os
import zlib
from pathlib import Path

import numpy as np

# id: (shape, colours dark to light). Keep in step with CosmeticCatalog.Patterns.
PATTERNS: dict[str, tuple[str, list[int]]] = {
    "woodland": ("blotch", [0x2A2416, 0x3E5A2A, 0x6E5A36, 0x5A7A3A]),
    "desert": ("blotch-soft", [0x6E5236, 0xA8875A, 0xD2B88A]),
    "digital": ("pixel", [0x2E3640, 0x56687A, 0x8A9AAE, 0xB4C2D0]),
    "red_tiger": ("stripe", [0x0A0A0A, 0xC01818]),
    "blue_tiger": ("stripe", [0x0A0A0A, 0x1C5AD8]),
    "golden": ("gold", [0x9A7020, 0xE6B840, 0xFFF2B0]),
    "pyre": ("flames", [0x0A0A0C, 0x6A0C02, 0xC82408, 0xF07010, 0xFFC038, 0xFFF4B8]),
    "kaido": ("livery", [0xE0301E, 0x1A2238, 0xF4F2EE, 0xF2C21A, 0x0C0C0E]),
    "cryo": ("cryo", [0xF0F2F4, 0x0E1014, 0x1E86D8, 0x8EDCFF]),
    "laser_grid": ("scanlines", [0x140808, 0xE82020]),
    "night_ops": ("blotch", [0x0C0C0E, 0x1C1C20, 0x2C2C30]),
    "static": ("noise", [0x101418, 0x5A6A7A, 0xD8E4F0]),
    "shockwave": ("arcs", [0x08101E, 0x2A8AFF, 0xC8F0FF]),
    "breaker": ("cracks", [0x2A2A2A, 0x6E6E6A, 0xA8A8A0]),
    "frag": ("shrapnel", [0x0E0C0A, 0x3A2A1E, 0xD86A1E]),
    "lockdown": ("hazard", [0x101010, 0xE8C018]),
    "smoke": ("smoke", [0x2A2C30, 0x5A5E66, 0x9AA0A8, 0xD0D4DA]),
    "chaff": ("chaff", [0x14161A, 0x7A828C, 0xE0E6EE]),
    "turnabout": ("turnabout", [0x1A0E08, 0x6A2A0E, 0xF07818]),
    "minefield": ("minefield", [0x1A2214, 0x3E4A2A, 0xE8C018]),
    "voltage": ("voltage", [0x0A0A10, 0x2A2A10, 0xF0D820, 0xFFF8C0]),
    "gadgeteer": ("cogs", [0x1A1C20, 0x4A5058, 0x8A929C, 0xC8A050]),
    "mastercraft": ("mastercraft", [0x2A1E0A, 0x9A7020, 0xE6B840, 0xFFF2B0]),
    "flashbang": ("flashes", [0x0C0E12, 0x3A4250, 0xBFD8FF, 0xFFFFFF]),
    "skeleton": ("skeleton", [0x0A0A0C, 0xE6DFC8, 0x8C846C, 0x030303]),
}

# Base maps and their palettes. Keep in step with CosmeticCatalog.Maps.
MAPS: dict[str, list[int]] = {
    "Aqua": [0x0B2A3A, 0x1F6E7E, 0x7FC8C8], "Bank": [0x2A2418, 0x6E5A2E, 0xC8B27A],
    "Cine": [0x2A0F14, 0x7A1E28, 0xC89A5A], "Clar": [0x1C2430, 0x4A6A8A, 0xC8D8E8],
    "Club": [0x1E1428, 0x5A2E6E, 0xC87AB2], "Cpus": [0x1A1E1A, 0x4E5A4A, 0xA8B49A],
    "Deft": [0x14181E, 0x3A4656, 0x9AA8B8], "Fact": [0x261A10, 0x6A4626, 0xB89A6A],
    "Ghos": [0x2A2620, 0x6E6250, 0xC8B89A], "Krau": [0x0E1E14, 0x2E6A3E, 0x9AD8A8],
    "Miss": [0x1E1E14, 0x5A5A2E, 0xB8B87A], "Moun": [0x1E2428, 0x56646E, 0xD8E0E4],
    "Muse": [0x2A1E14, 0x7A5A36, 0xD8C29A], "Offs": [0x101418, 0x3A2E1E, 0xD88A2E],
    "Orph": [0x1A1A22, 0x46465A, 0xA8A0B8], "Pana": [0x0E2014, 0x2E5A2A, 0xC8C27A],
    "Pola": [0x2A3440, 0x8AA0B4, 0xF0F4F8], "Pris": [0x202020, 0x585048, 0xB0A890],
    "Pyro": [0x200A06, 0x8A2A0E, 0xF0A030], "Rive": [0x14202A, 0x2E5A7A, 0xC8D8C2],
    "Sanc": [0x1E140E, 0x5A3A22, 0xC8A27A], "Sche": [0x161616, 0x4A3E36, 0xB8A898],
    "Shad": [0x1A2228, 0x52606A, 0xE4ECF0], "Skys": [0x10141E, 0x2E3E6E, 0xA8B8E8],
    "Slau": [0x161414, 0x2E2A2A, 0x5A0C10, 0x9A161C], "Stat": [0x1A1A14, 0x5A5236, 0xC2B47A],
    "Stee": [0x14161A, 0x464E5A, 0xA8AEB8], "Stro": [0x1A1810, 0x4E4630, 0x9A8E6A],
    "Term": [0x101A1E, 0x2E4E56, 0x8AB8B8], "Vert": [0x0E0E1A, 0x3A2E6E, 0xE89A3A],
    "Ware": [0x1E1A14, 0x5A4E3A, 0xA8987A], "Ziop": [0x140E1E, 0x46226A, 0x3AE8D8],
}
# Maps whose camo is not the shared blotch. Keep in step with CosmeticCatalog's map patterns.
_MAP_SHAPES = {"Slau": "splatter"}
for _code, _palette in MAPS.items():
    PATTERNS["map_" + _code.lower()] = (_MAP_SHAPES.get(_code, "blotch"), _palette)

_LADDER = ["woodland", "desert", "digital", "red_tiger", "blue_tiger"]
_MAP_PATTERNS = ["map_" + c.lower() for c in MAPS]
_SPY_EXTRA = ["smoke", "chaff", "turnabout", "minefield", "gadgeteer", "mastercraft", "flashbang"]
_MERC_EXTRA = ["voltage", "minefield", "gadgeteer", "mastercraft"]

# Which retail textures take which patterns: (chars package folder, texture file stem, patterns).
TARGETS: list[tuple[str, str, list[str]]] = [
    ("SPerso", "SPersoTextures_Shadw_agent", _LADDER + ["night_ops", "static", "breaker"] + _SPY_EXTRA + _MAP_PATTERNS
     + ["skeleton"]),
    ("SPerso", "SPersoTextures_Shadw_agent_2", _LADDER + ["night_ops", "static", "breaker"] + _SPY_EXTRA + _MAP_PATTERNS
     + ["skeleton"]),
    ("SPerso", "SPersoTextures_DEF_01_A_Body", _LADDER + ["laser_grid", "frag", "lockdown"] + _MERC_EXTRA + _MAP_PATTERNS
     + ["skeleton"]),
    ("SPerso", "SPersoTextures_DEF_01_A_Face", ["skeleton"]),  # the skull covers the merc's face too
    ("SWeaponFirst", "SWeaponTex_p190", _LADDER + ["static", "shockwave", "golden", "pyre", "kaido", "cryo",
                                                   "chaff", "turnabout", "gadgeteer", "smoke", "mastercraft", "flashbang"]),
]
# The skeleton is drawn from the body's bind pose (svmbuild/skeleton.py): the mesh each body texture is painted from.
SKELETON_MESH = {"SPersoTextures_Shadw_agent": "ATT_01.glb", "SPersoTextures_Shadw_agent_2": "ATT_01.glb",
                 "SPersoTextures_DEF_01_A_Body": "DEF_01.glb", "SPersoTextures_DEF_01_A_Face": "DEF_01.glb"}
for _gun in ("SWeaponTex_1DEF_01", "SWeaponTex_M160", "SWeaponTex_Hk81", "SWeaponTex_HK_81_chargeur", "SWeaponTex_shotgun"):
    TARGETS.append(("SWeaponFirst", _gun, _LADDER + ["golden", "laser_grid", "frag", "pyre", "kaido", "cryo"] + _MERC_EXTRA))

# Output size: the retail size times the package's factor (bodies 512 -> 2048, guns 256 -> 1024), at least MIN_SIZE on
# the long side. The map camos on bodies stop at MAP_MAX.
SCALE = {"SPerso": 4, "SWeaponFirst": 4}
MIN_SIZE = 64
MAP_MAX = 1024
THUMB_SIZE = 256

# How much of the camo replaces the retail colour (the rest keeps small markings readable). The finishes (gold, the
# painted skins: pyre, kaido, cryo, the cogs) replace nearly all of it, or the retail grey dulls them.
CAMO_STRENGTH = 0.85
FINISH_STRENGTH = 0.97
_GILDED = ("gold", "mastercraft")  # colours follow the retail brightness, no extra shading
_LACQUERED = ("flames", "livery", "cryo", "cogs", "skeleton")  # a light touch of the retail shading
_FINISHES = _GILDED + _LACQUERED
# Shapes that get the shared fine-detail layer (grain, hairline scratches, scuffs).
_WEATHERED = ("blotch", "blotch-soft", "pixel", "stripe", "cracks", "shrapnel", "hazard", "smoke", "chaff",
              "turnabout", "minefield", "voltage", "cogs", "arcs", "flashes", "splatter")

_LIGHT = (-0.6, -0.8)  # unit vector toward the light (top left), x right / y down
_TAU = 2.0 * np.pi


# ---------------------------------------------------------------------------------------------------------------------
# Small helpers

def _rgb(c: int) -> np.ndarray:
    return np.array([(c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF], dtype=np.float32) / 255.0


def _rng(pattern: str, seed: str) -> np.random.Generator:
    """Fixed per pattern and texture (never per size), so a camo is the same design at any size."""
    return np.random.default_rng(zlib.crc32(f"{pattern}:{seed}".encode()))


def _ss(e0: float, e1: float, x):
    t = np.clip((x - e0) / (e1 - e0), 0.0, 1.0)
    return t * t * (3.0 - 2.0 * t)


def _mix(a, b, t):
    """a to b by t (t h x w, colours (3,) or h x w x 3)."""
    return a + (b - a) * np.asarray(t, np.float32)[..., None]


def _cov(d, px: float):
    """Coverage of a shape from its signed distance d (negative inside), anti-aliased over one pixel px."""
    return np.clip(0.5 - d / px, 0.0, 1.0)


def _ink(d, hw: float, px: float):
    """Coverage of a line of half width hw from the distance d to its centre line. A line under a pixel wide is drawn
    a pixel wide and fainter, so hairlines fade rather than break up at small sizes."""
    he = max(hw, 0.5 * px)
    return np.clip((he - d) / px + 0.5, 0.0, 1.0) * (hw / he)


def _edge(s, soft: float = 0.0):
    """Anti-aliased coverage of s > 0 for a smooth field s: blended over one pixel (plus soft, in s units)."""
    gy, gx = np.gradient(s)
    return np.clip(s / (np.hypot(gx, gy) + soft + 1e-6) + 0.5, 0.0, 1.0)


def _stripe(s, duty: float, fw):
    """Coverage of the first duty of each period of s (s counted in periods, fw its change per pixel)."""
    d = duty / 2 - np.abs(((s - duty / 2 + 0.5) % 1.0) - 0.5)
    return np.clip(d / fw + 0.5, 0.0, 1.0)


def _span(f, lo: float, hi: float, fw):
    """Coverage of lo < f < hi, anti-aliased (fw: the change of f per pixel)."""
    return np.clip((f - lo) / fw + 0.5, 0.0, 1.0) * np.clip((hi - f) / fw + 0.5, 0.0, 1.0)


def _ramp(t, colours: list[int]) -> np.ndarray:
    """A smooth gradient through the colours for t in 0..1."""
    palette = np.stack([_rgb(c) for c in colours])
    t = np.clip(t, 0.0, 1.0) * (len(colours) - 1)
    i = np.clip(t.astype(np.int32), 0, len(colours) - 2)
    f = (t - i)[..., None]
    return palette[i] * (1 - f) + palette[i + 1] * f


def _bands(value, colours: list[int], soft: float = 0.0) -> np.ndarray:
    """value 0..1 split into equal bands of the colours, each edge anti-aliased (soft widens it, in value units)."""
    gy, gx = np.gradient(value)
    fw = np.hypot(gx, gy) + soft + 1e-6
    palette = np.stack([_rgb(c) for c in colours])
    out = np.empty(value.shape + (3,), np.float32)
    out[:] = palette[0]
    for k in range(1, len(colours)):
        a = np.clip((value - k / len(colours)) / fw + 0.5, 0.0, 1.0)
        out += (palette[k] - palette[k - 1]) * a[..., None]
    return out


def _uv(h: int, w: int):
    """Pixel centres in texture units (the long side is 1): x (1 x w) and y (h x 1)."""
    L = max(h, w)
    return (((np.arange(w, dtype=np.float32) + 0.5) / L)[None, :],
            ((np.arange(h, dtype=np.float32) + 0.5) / L)[:, None])


def _rot(x, y, a: float):
    """The point turned by -a (into a frame turned by a)."""
    c, s = np.cos(a), np.sin(a)
    return c * x + s * y, -s * x + c * y


def _blur(img, sigma: float, px: float):
    """Gaussian blur, sigma in texture units, wrapping across the edges (the same kernel as scipy's gaussian_filter:
    cut off at 4 sigma, run along each axis in turn)."""
    s = max(sigma / px, 0.3)
    radius = int(4.0 * s + 0.5)
    taps = np.exp(-0.5 * (np.arange(-radius, radius + 1, dtype=np.float64) / s) ** 2)
    taps /= taps.sum()
    out = np.asarray(img, dtype=np.float64)
    for axis in range(out.ndim):
        acc = np.zeros_like(out)
        for k, t in zip(range(-radius, radius + 1), taps):
            acc += t * np.roll(out, -k, axis=axis)
        out = acc
    return out.astype(np.asarray(img).dtype)


# ---------------------------------------------------------------------------------------------------------------------
# Noise (all measured in cells across the texture, so it does not change with the size)

def _noise(rng: np.random.Generator, h: int, w: int, cy: int, cx: int | None = None) -> np.ndarray:
    """Value noise: a random grid of cy x cx cells, bicubically enlarged to h x w; it wraps across the edges."""
    from PIL import Image  # noqa: PLC0415

    cx = cx or cy
    grid = rng.random((cy, cx), dtype=np.float32)
    pad = max(2, int(2 * max(cx / w, cy / h)) + 2)
    grid = np.pad(grid, pad, mode="wrap")
    img = Image.fromarray(grid).resize((w, h), Image.BICUBIC, box=(pad, pad, pad + cx, pad + cy))
    return np.asarray(img, dtype=np.float32)


def _norm(x: np.ndarray) -> np.ndarray:
    lo, hi = np.percentile(x, (1, 99))
    return np.clip((x - lo) / max(float(hi - lo), 1e-6), 0.0, 1.0).astype(np.float32)


def _fbm(rng: np.random.Generator, h: int, w: int, base: int, octaves: int = 3, gain: float = 0.5) -> np.ndarray:
    """Layered value noise from base cells across, each octave twice as fine; stretched to 0..1."""
    out = np.zeros((h, w), np.float32)
    amp, cells = 1.0, base
    for _ in range(octaves):
        out += amp * _noise(rng, h, w, cells)
        amp *= gain
        cells *= 2
    return _norm(out)


def _warp(img: np.ndarray, du, dv) -> np.ndarray:
    """img sampled with each pixel moved by (du, dv) texture units (wraps)."""
    h, w = img.shape
    L = max(h, w)
    yy = np.arange(h, dtype=np.float32)[:, None] + np.asarray(dv, np.float32) * L
    xx = np.arange(w, dtype=np.float32)[None, :] + np.asarray(du, np.float32) * L
    yy, xx = np.broadcast_arrays(yy, xx)
    # Bilinear between the four wrapped neighbours.
    y0, x0 = np.floor(yy), np.floor(xx)
    fy, fx = (yy - y0).astype(np.float32), (xx - x0).astype(np.float32)
    y0, x0 = y0.astype(np.int64) % h, x0.astype(np.int64) % w
    y1, x1 = (y0 + 1) % h, (x0 + 1) % w
    src = np.asarray(img, dtype=np.float32)
    top = src[y0, x0] * (1 - fx) + src[y0, x1] * fx
    bottom = src[y1, x0] * (1 - fx) + src[y1, x1] * fx
    return (top * (1 - fy) + bottom * fy).astype(np.float32)


def _blocks(small: np.ndarray, h: int, w: int) -> np.ndarray:
    """A small block image (n x m x c) enlarged to h x w with crisp edges: each block repeated whole, then area-scaled
    to the size (edges land on pixel edges where the sizes divide, else blend over one pixel)."""
    from PIL import Image  # noqa: PLC0415

    n, m = small.shape[:2]
    big = small.repeat(-(-h // n), 0).repeat(-(-w // m), 1)
    if big.shape[:2] == (h, w):
        return big.astype(np.float32)
    chans = [np.asarray(Image.fromarray(np.ascontiguousarray(big[..., c], np.float32)).resize((w, h), Image.BOX))
             for c in range(small.shape[2])]
    return np.stack(chans, -1).astype(np.float32)


def _brushed(rng: np.random.Generator, h: int, w: int) -> np.ndarray:
    """Brushed-metal streaks along x: fine, mid and coarse streak noise, about 0..1."""
    return _norm(0.5 * _noise(rng, h, w, 900, 5) + 0.3 * _noise(rng, h, w, 300, 3) + 0.2 * _noise(rng, h, w, 60, 2))


# ---------------------------------------------------------------------------------------------------------------------
# Canvas: an RGB image addressed in texture units, with small wrapping tiles for stamping motifs

class _Canvas:
    def __init__(self, h: int, w: int, rgb: np.ndarray | None = None):
        self.h, self.w = h, w
        self.L = max(h, w)
        self.px = 1.0 / self.L
        self.W, self.H = w / self.L, h / self.L
        self.rgb = rgb if rgb is not None else np.zeros((h, w, 3), np.float32)

    def tile(self, cx: float, cy: float, r: float):
        """The pixels within r of (cx, cy) (a square, wrapped across the edges): row and column indices and each
        pixel's offset from the centre (X, Y, texture units)."""
        L = self.L
        x0, y0 = int(np.floor((cx - r) * L)), int(np.floor((cy - r) * L))
        x1 = min(max(int(np.ceil((cx + r) * L)) + 1, x0 + 2), x0 + self.w)
        y1 = min(max(int(np.ceil((cy + r) * L)) + 1, y0 + 2), y0 + self.h)
        xs, ys = np.arange(x0, x1), np.arange(y0, y1)
        X, Y = np.meshgrid(((xs + 0.5) / L - cx).astype(np.float32), ((ys + 0.5) / L - cy).astype(np.float32))
        return (ys % self.h)[:, None], (xs % self.w)[None, :], X, Y

    def over(self, iy, ix, alpha, colour) -> None:
        sub = self.rgb[iy, ix]
        self.rgb[iy, ix] = sub + (colour - sub) * alpha[..., None]

    def add(self, iy, ix, amount, colour) -> None:
        """Additive light (glows), clipped to white."""
        self.rgb[iy, ix] = np.minimum(self.rgb[iy, ix] + np.asarray(colour) * amount[..., None], 1.0)

    def darken(self, iy, ix, amount) -> None:
        self.rgb[iy, ix] *= (1.0 - amount)[..., None]


def _relief(cv: _Canvas, iy, ix, d, colour, bevel: float, relief: float = 0.5, gloss: float = 0.0) -> None:
    """Draws a raised shape from its signed distance d (texture units): within bevel of its edge the surface tilts and
    is lit from the top left (gloss adds a highlight on the lit rim)."""
    a = _cov(d, cv.px)
    if not a.any():
        return
    gy, gx = np.gradient(d, cv.px)
    n = np.hypot(gx, gy) + 1e-6
    lit = (gx * _LIGHT[0] + gy * _LIGHT[1]) / n
    rim = np.clip(1.0 + d / max(bevel, 1.5 * cv.px), 0.0, 1.0)
    col = np.asarray(colour, np.float32) * (1.0 + relief * lit * rim)[..., None]
    if gloss:
        col = col + (gloss * np.clip(lit, 0.0, 1.0) ** 3 * rim)[..., None]
    cv.over(iy, ix, a, np.clip(col, 0.0, 1.0))


def _shadow(cv: _Canvas, iy, ix, ds, soft: float, strength: float = 0.5) -> None:
    """A soft shadow from a shape's signed distance ds taken at shifted coordinates."""
    cv.darken(iy, ix, strength * np.clip(0.5 - ds / soft, 0.0, 1.0))


def _dome(dx, dy, r: float, px: float):
    """A round dome (rivet, screw head) of radius r at offset (dx, dy): coverage, shade (about 0.5..1.3) and its
    specular highlight."""
    d = np.hypot(dx, dy)
    nx, ny = np.clip(dx / r, -1, 1), np.clip(dy / r, -1, 1)
    nz = np.sqrt(np.clip(1.0 - nx * nx - ny * ny, 0.0, 1.0))
    lit = nx * _LIGHT[0] + ny * _LIGHT[1]
    shade = 0.6 + 0.3 * nz + 0.45 * lit
    spec = np.exp(-((nx + 0.35) ** 2 + (ny + 0.45) ** 2) / 0.05)
    return _cov(d - r, px), shade, spec


def _segments(cv: _Canvas, field: np.ndarray, segs, reach: float) -> None:
    """Lowers field (h x w, texture units) to the distance from each segment's edge; segs rows (ax, ay, bx, by, half
    width). reach: how far out from a segment the distance is kept (for glows)."""
    for ax, ay, bx, by, hw in segs:
        cx, cy = (ax + bx) / 2, (ay + by) / 2
        iy, ix, X, Y = cv.tile(cx, cy, np.hypot(bx - ax, by - ay) / 2 + hw + reach)
        d = _sd_segment(X, Y, ax - cx, ay - cy, bx - cx, by - cy) - hw
        field[iy, ix] = np.minimum(field[iy, ix], d)
    # Beyond reach the distance is not kept: cap it there, so the field has no jumps at the tile borders.
    np.minimum(field, reach, out=field)


def _glow(cv: _Canvas, cx: float, cy: float, r: float, colour, gain: float = 1.0, stretch: float = 1.0,
          angle: float = 0.0) -> None:
    """A small glowing dot (spark, ember, node): a hot core of radius r and a halo, optionally stretched along angle."""
    iy, ix, X, Y = cv.tile(cx, cy, r * 5 * stretch)
    x, y = _rot(X, Y, angle)
    d = np.hypot(x / stretch, y)
    amount = np.maximum(_cov(d - r, cv.px), 0.55 * np.exp(-d / (r * 1.4)))
    cv.add(iy, ix, amount * gain, colour)


# ---------------------------------------------------------------------------------------------------------------------
# Signed-distance motifs (local units: the motif's radius is 1)

def _sd_circle(x, y, r):
    return np.hypot(x, y) - r


def _sd_box(x, y, hx, hy, rr=0.0):
    qx, qy = np.abs(x) - hx + rr, np.abs(y) - hy + rr
    return np.hypot(np.maximum(qx, 0), np.maximum(qy, 0)) + np.minimum(np.maximum(qx, qy), 0) - rr


def _sd_segment(x, y, ax, ay, bx, by):
    pax, pay, bax, bay = x - ax, y - ay, bx - ax, by - ay
    t = np.clip((pax * bax + pay * bay) / max(bax * bax + bay * bay, 1e-12), 0.0, 1.0)
    return np.hypot(pax - bax * t, pay - bay * t)


def _sd_hex(x, y, r):
    """A hexagon of inradius r (flat top and bottom)."""
    kx, ky, kz = -0.866025404, 0.5, 0.577350269
    x, y = np.abs(x), np.abs(y)
    dot = np.minimum(kx * x + ky * y, 0.0)
    x, y = x - 2 * dot * kx, y - 2 * dot * ky
    x = x - np.clip(x, -kz * r, kz * r)
    y = y - r
    return np.hypot(x, y) * np.sign(y)


def _sd_polygon(x, y, pts):
    """A convex polygon (vertices in angle order around the origin): the furthest of its edges' half planes."""
    d = None
    for i in range(len(pts)):
        (ax, ay), (bx, by) = pts[i], pts[(i + 1) % len(pts)]
        ex, ey = bx - ax, by - ay
        ln = np.hypot(ex, ey)
        e = ((x - ax) * ey - (y - ay) * ex) / ln
        d = e if d is None else np.maximum(d, e)
    return d


def _fold(x, y, n: int):
    """The point turned into the sector of the nearest of n copies around the origin (copy 0 along +x)."""
    th = np.arctan2(y, x)
    k = np.round(th / (_TAU / n))
    r = np.hypot(x, y)
    return r * np.cos(th - k * _TAU / n), r * np.sin(th - k * _TAU / n)


def _sd_gear(x, y, teeth: int, root: float, pitch: float, hw_pitch: float):
    """A gear's outline (tip at radius 1): teeth tapering from the root to the tip, hw_pitch the tooth's half width at
    the pitch circle."""
    fx, fy = _fold(x, y, teeth)
    t = (fx - pitch) / (1.0 - pitch)
    hw = hw_pitch * (1.0 - 0.38 * t)
    tooth = np.maximum(np.maximum(np.abs(fy) - hw, fx - 1.0), root - 0.05 - fx)
    return np.minimum(np.hypot(x, y) - root, tooth)


def _sd_chevron(x, y, t=0.16):
    """A chevron pointing up, arms t thick."""
    return np.minimum(_sd_segment(x, y, -0.6, 0.35, 0.0, -0.3), _sd_segment(x, y, 0.6, 0.35, 0.0, -0.3)) - t


def _sd_spiral(x, y, turns: float):
    """Distance to an Archimedean spiral from near the centre out to radius 1 after `turns` turns."""
    a = 1.0 / (_TAU * turns)
    r = np.hypot(x, y)
    th = np.arctan2(y, x) % _TAU
    k0 = np.floor((r / a - th) / _TAU)
    lo, hi = 0.8, _TAU * turns
    best = None
    for k in (k0 - 1, k0, k0 + 1):
        phi = th + _TAU * k
        d = np.where((phi >= lo) & (phi <= hi), np.abs(r - a * phi), np.inf)
        best = d if best is None else np.minimum(best, d)
    for phi in (lo, hi):  # the two ends
        best = np.minimum(best, np.hypot(x - a * phi * np.cos(phi), y - a * phi * np.sin(phi)))
    return best


def _sd_leaf(x, y, ax, ay, bx, by, wmax):
    """A leaf (pointed at both ends, widest in the middle) from a to b: its signed distance and its centre line's."""
    pax, pay, bax, bay = x - ax, y - ay, bx - ax, by - ay
    t = np.clip((pax * bax + pay * bay) / (bax * bax + bay * bay), 0.0, 1.0)
    d = np.hypot(pax - bax * t, pay - bay * t)
    return d - wmax * np.sin(np.pi * t), d


def _sd_mine(x, y, prongs: int):
    """A round mine seen from above as a stencil: a disc with a cut ring (held by four bridges), the pressure plate in
    the middle and prongs with ball tips around it."""
    r = np.hypot(x, y)
    body = r - 0.6
    bx, by = _fold(x, y, 4)
    cut = np.maximum(np.abs(r - 0.42) - 0.05, -_sd_box(bx - 0.42, by, 0.12, 0.06))
    body = np.maximum(body, -cut)
    px_, py_ = _fold(*_rot(x, y, np.pi / prongs), prongs)
    prong = np.minimum(_sd_box(px_ - 0.72, py_, 0.16, 0.05), _sd_circle(px_ - 0.9, py_, 0.1))
    return np.minimum(body, prong)


# ---------------------------------------------------------------------------------------------------------------------
# Placement

def _scatter(rng: np.random.Generator, tries: int, r_lo: float, r_hi: float, gap: float = 1.0, W: float = 1.0,
             H: float = 1.0, avoid=None, bias: float = 1.5):
    """Poisson-disc-ish placement on the wrapping texture: from `tries` random candidates, those closer to an accepted
    one than gap x their radii summed are dropped. Returns arrays x, y, radius, angle, extra (0..1), the radii from
    r_lo to r_hi (bias > 1 favours small ones). avoid: (x, y, r) arrays to keep clear of."""
    cand = rng.random((tries, 5))
    xs, ys = cand[:, 0] * W, cand[:, 1] * H
    rs = r_lo + (r_hi - r_lo) * cand[:, 2] ** bias
    keep = np.zeros(tries, bool)
    ax, ay, ar = np.empty(tries), np.empty(tries), np.empty(tries)
    n = 0
    for i in range(tries):
        if n and _too_close(ax[:n], ay[:n], ar[:n], xs[i], ys[i], rs[i], gap, W, H):
            continue
        if avoid is not None and len(avoid[0]) and _too_close(avoid[0], avoid[1], avoid[2], xs[i], ys[i], rs[i],
                                                              gap, W, H):
            continue
        ax[n], ay[n], ar[n] = xs[i], ys[i], rs[i]
        keep[i] = True
        n += 1
    return xs[keep], ys[keep], rs[keep], cand[keep, 3] * _TAU, cand[keep, 4]


def _too_close(ax, ay, ar, x, y, r, gap, W, H) -> bool:
    dx = ax - x
    dx -= W * np.round(dx / W)
    dy = ay - y
    dy -= H * np.round(dy / H)
    return bool(np.any(dx * dx + dy * dy < (gap * (ar + r)) ** 2))


def _lightning(rng: np.random.Generator, ax, ay, bx, by, hw: float, depth: int = 5, branch: float = 0.35,
               jitter: float = 0.22, out: list | None = None) -> list:
    """A jittered bolt from a to b by midpoint displacement, with recursive side branches: segment rows (ax, ay, bx,
    by, half width), tapering along the bolt."""
    out = [] if out is None else out
    pts = np.array([[ax, ay], [bx, by]], np.float64)
    for _ in range(depth):
        d = pts[1:] - pts[:-1]
        mids = (pts[:-1] + pts[1:]) / 2 + np.stack([-d[:, 1], d[:, 0]], 1) * rng.normal(0, jitter, len(d))[:, None]
        new = np.empty((len(pts) + len(mids), 2))
        new[0::2], new[1::2] = pts, mids
        pts = new
    n = len(pts) - 1
    length = float(np.hypot(bx - ax, by - ay))
    rolls = rng.random((n, 3))
    for i in range(n):
        w = hw * (1.0 - 0.55 * i / n)
        out.append((pts[i, 0], pts[i, 1], pts[i + 1, 0], pts[i + 1, 1], w))
        if depth > 2 and 0 < i < n - 1 and rolls[i, 0] < branch * 4 / n:
            dx, dy = pts[i + 1] - pts[i]
            ang = np.arctan2(dy, dx) + (0.35 + 0.5 * rolls[i, 1]) * (1 if rolls[i, 2] < 0.5 else -1)
            ln = length * (0.25 + 0.3 * rolls[i, 1]) * (1 - i / n)
            _lightning(rng, pts[i, 0], pts[i, 1], pts[i, 0] + ln * np.cos(ang), pts[i, 1] + ln * np.sin(ang),
                       w * 0.6, depth - 1, branch * 0.7, jitter, out)
    return out


def _crack(rng: np.random.Generator, x, y, heading, steps: int, step: float, hw: float, depth: int, out: list) -> list:
    """A crack walking from (x, y) with a wavering heading, thinning as it goes and splitting now and then."""
    rolls = rng.random((steps, 4))
    for i in range(steps):
        heading += (rolls[i, 0] - 0.5) * 0.55
        ln = step * (0.6 + 0.8 * rolls[i, 1])
        nx, ny = x + ln * np.cos(heading), y + ln * np.sin(heading)
        w = hw * (1.0 - 0.7 * i / steps)
        out.append((x, y, nx, ny, w))
        if depth > 0 and rolls[i, 2] < 0.13:
            side = 1 if rolls[i, 3] < 0.5 else -1
            _crack(rng, nx, ny, heading + side * (0.5 + 0.6 * rolls[i, 3]), max(2, (steps - i) // 2), step, w * 0.7,
                   depth - 1, out)
        x, y = nx, ny
    return out


# ---------------------------------------------------------------------------------------------------------------------
# Stamped motifs (centre and size in texture units)

def _rivet(cv: _Canvas, cx, cy, r, colour) -> None:
    """A domed rivet with a soft shadow and a highlight."""
    iy, ix, X, Y = cv.tile(cx, cy, r * 1.9)
    _shadow(cv, iy, ix, np.hypot(X - 0.35 * r, Y - 0.45 * r) - r, r * 0.6, 0.45)
    a, shade, spec = _dome(X, Y, r, cv.px)
    col = np.asarray(colour) * shade[..., None] + 0.5 * spec[..., None]
    cv.over(iy, ix, a, np.clip(col, 0, 1))
    cv.darken(iy, ix, 0.35 * _ink(np.abs(np.hypot(X, Y) - r), r * 0.08, cv.px))


def _hex_bolt(cv: _Canvas, cx, cy, size, angle, colour) -> None:
    """A hex bolt head on a round washer, both bevelled, with a chamfer ring on the head."""
    iy, ix, X, Y = cv.tile(cx, cy, size * 1.35)
    x, y = _rot(X, Y, angle)
    _shadow(cv, iy, ix, (np.hypot(X - 0.12 * size, Y - 0.16 * size) - size), size * 0.25, 0.5)
    washer = np.asarray(colour) * 0.82
    _relief(cv, iy, ix, (np.hypot(x, y) - size), washer, size * 0.18, 0.6)
    _relief(cv, iy, ix, _sd_hex(x / size, y / size, 0.7) * size, colour, size * 0.16, 0.75, gloss=0.2)
    r = np.hypot(x, y) / size
    cv.over(iy, ix, 0.25 * _cov((r - 0.52) * size, cv.px) * _cov(_sd_hex(x / size, y / size, 0.7) * size, cv.px),
            np.asarray(colour) * 1.15)
    cv.darken(iy, ix, 0.3 * _ink(np.abs(r - 0.52) * size, size * 0.025, cv.px))


def _cog(cv: _Canvas, cx, cy, r, angle, teeth, module, colour, kind: float) -> None:
    """A machined cog of outer radius r: bevelled teeth, a lathe-turned face with an engraved ring, spokes (or round
    lightening holes on small cogs), a raised hub with four small screws and the axle hole."""
    m = module / np.pi  # gear module: addendum m, dedendum 1.25 m
    pitch = (r - m) / r
    root = (r - 2.25 * m) / r
    hwp = (module / 4) / r
    spokes = 0 if r < 0.011 else 3 + int(kind * 4)
    iy, ix, X, Y = cv.tile(cx, cy, r * 1.25)

    def body(x, y):
        g = _sd_gear(x, y, teeth, root, pitch, hwp)
        rr = np.hypot(x, y)
        if spokes:
            fx, fy = _fold(x, y, spokes)
            holes = np.maximum(np.maximum(rr - (root - 0.16), 0.36 - rr), -(np.abs(fy) - 0.075))
            g = np.maximum(g, -holes)
        elif r > 0.0075:
            hx, hy = _fold(*_rot(x, y, np.pi / 5), 5)
            g = np.maximum(g, -_sd_circle(hx - 0.5 * root, hy, 0.13 * root))
        return np.maximum(g, 0.08 - rr)

    # The shadow it casts on what lies under it, then the face.
    off = r * 0.09
    sx, sy = _rot(X - off * 0.6, Y - off * 0.8, angle)
    _shadow(cv, iy, ix, body(sx / r, sy / r) * r, r * 0.12, 0.55)
    x, y = _rot(X, Y, angle)
    x, y = x / r, y / r
    rr = np.hypot(x, y)
    d = body(x, y) * r
    lathe = 1.0 + 0.045 * np.sin(rr * _TAU * 11 + kind * 7) + 0.025 * np.sin(rr * _TAU * 37)
    _relief(cv, iy, ix, d, np.asarray(colour) * lathe[..., None], r * 0.07, 0.6, gloss=0.18)
    # An engraved ring inside the teeth.
    cv.darken(iy, ix, 0.35 * _ink(np.abs(rr - (root - 0.08)) * r, r * 0.012, cv.px) * _cov(d, cv.px))
    # The hub: a raised boss, darker, with four screws and the axle hole.
    hub = np.maximum(rr - 0.3, 0.08 - rr) * r
    _relief(cv, iy, ix, hub, np.asarray(colour) * 0.85, r * 0.05, 0.7, gloss=0.15)
    for k in range(4):
        a = angle + k * _TAU / 4 + np.pi / 4
        sxk, syk = X - np.cos(a) * 0.19 * r, Y - np.sin(a) * 0.19 * r
        sa, shade, spec = _dome(sxk, syk, 0.045 * r, cv.px)
        cv.over(iy, ix, sa, np.clip(np.asarray(colour) * 0.95 * shade[..., None] + 0.4 * spec[..., None], 0, 1))
        slot = _rot(sxk, syk, angle + k)
        cv.darken(iy, ix, 0.6 * sa * _ink(np.abs(slot[1]), 0.008 * r, cv.px))


def _cog_layout(rng: np.random.Generator, clusters: int, grow: int, r_lo: float, r_hi: float, module: float,
                W: float = 1.0, H: float = 1.0) -> list[tuple]:
    """Clusters of interlocking cogs: each cluster starts from one cog and grows cogs meshing with its members (pitch
    circles touching, teeth phased into the neighbour's gaps), never overlapping any other cog. Rows (x, y, r, angle,
    teeth, extra)."""
    m = module / np.pi
    cogs: list[tuple] = []
    cx_, cy_, cr_ = np.empty(clusters * (grow + 1)), np.empty(clusters * (grow + 1)), np.empty(clusters * (grow + 1))
    seeds = rng.random((clusters, 5))
    growth = rng.random((clusters, grow, 4))

    def sized(t):
        teeth = max(8, int(round(_TAU * (r_lo + (r_hi - r_lo) * t ** 1.4 - m) / module)))
        return teeth, teeth * module / _TAU + m  # teeth, outer radius

    def clear(x, y, r, skip):
        n = len(cogs)
        dx = (cx_[:n] - x + W / 2) % W - W / 2
        dy = (cy_[:n] - y + H / 2) % H - H / 2
        hit = dx * dx + dy * dy < ((cr_[:n] + r) * 1.03) ** 2
        if 0 <= skip < n:
            hit[skip] = False
        return not hit.any()

    def add(c):
        cx_[len(cogs)], cy_[len(cogs)], cr_[len(cogs)] = c[0], c[1], c[2]
        cogs.append(c)

    for c in range(clusters):
        teeth, r = sized(seeds[c, 2])
        x, y = seeds[c, 0] * W, seeds[c, 1] * H
        if not clear(x, y, r, -1):
            continue
        members = [len(cogs)]
        add((x, y, r, seeds[c, 3] * _TAU, teeth, seeds[c, 4]))
        for g in range(grow):
            pi = members[int(growth[c, g, 0] * len(members))]
            p = cogs[pi]
            teeth, r = sized(growth[c, g, 1])
            phi = growth[c, g, 2] * _TAU
            dist = (p[2] - m) + (r - m)
            x, y = (p[0] + dist * np.cos(phi)) % W, (p[1] + dist * np.sin(phi)) % H
            if not clear(x, y, r, pi):
                continue
            delta = ((phi - p[3]) / (_TAU / p[4])) % 1.0
            members.append(len(cogs))
            add((x, y, r, phi + np.pi + (delta - 0.5) * _TAU / teeth, teeth, growth[c, g, 3]))
    return cogs


# ---------------------------------------------------------------------------------------------------------------------
# Shared fine detail

def _fine_detail(rng: np.random.Generator, rgb: np.ndarray) -> None:
    """A subtle wear layer for the camos: micro grain, a few faint scuffs and hairline scratches."""
    h, w = rgb.shape[:2]
    cv = _Canvas(h, w, rgb)
    grain = (_noise(rng, h, w, 700) - 0.5) * 0.05 + (_noise(rng, h, w, 1400) - 0.5) * 0.035
    rgb *= (1.0 + grain)[..., None]
    scuff = _ss(0.80, 0.97, _fbm(rng, h, w, 10, 3)) * 0.07
    rgb += ((1.0 - rgb) * 0.5 * scuff[..., None])
    field = np.full((h, w), 1.0, np.float32)
    segs = []
    for x, y, ang, ln, bend in rng.random((40, 5)):
        x, y, ang, ln = x * cv.W, y * cv.H, ang * _TAU, 0.01 + 0.045 * ln
        for _ in range(3):
            nx, ny = x + ln / 3 * np.cos(ang), y + ln / 3 * np.sin(ang)
            segs.append((x, y, nx, ny, 0.0))
            x, y, ang = nx, ny, ang + (bend - 0.5) * 0.3
    _segments(cv, field, segs, 2 * cv.px)
    rgb += (1.0 - rgb) * (0.16 * _ink(np.maximum(field, 0), 0.00022, cv.px))[..., None]


# ---------------------------------------------------------------------------------------------------------------------
# Shapes: each returns the pattern's colours, h x w x 3 in 0..1

def _shape_blotch(rng, h, w, colours, luma, soft=False):
    """Woodland-style blotches: layered value noise with its domain warped, roughened by two fine octaves so the blotch
    edges are fractal; hard anti-aliased bands (desert: softened bands plus a fine sand speckle)."""
    base = _fbm(rng, h, w, 3 if soft else 4, 4)
    wu, wv = _fbm(rng, h, w, 6, 3) - 0.5, _fbm(rng, h, w, 6, 3) - 0.5
    value = _warp(base, wu * 0.06, wv * 0.06)
    value = value + (_noise(rng, h, w, 64) - 0.5) * (0.05 if soft else 0.10) \
        + (_noise(rng, h, w, 160) - 0.5) * (0.02 if soft else 0.05)
    out = _bands(_norm(value), colours, soft=0.07 if soft else 0.0)
    out *= (0.96 + 0.08 * _noise(rng, h, w, 40))[..., None]
    if soft:
        g1, g2 = _noise(rng, h, w, 520), _noise(rng, h, w, 900)
        out = _mix(out, _rgb(colours[-1]) * 1.06, 0.55 * _ss(0.72, 0.84, g1))
        out = _mix(out, _rgb(colours[0]) * 0.9, 0.45 * _ss(0.28, 0.16, g2))
    return out


def _shape_pixel(rng, h, w, colours, luma):
    """Digital camo on two block grids (1/64 and 1/128 of the texture): big blocks carry the shapes, small blocks fray
    their edges. Drawn at block resolution, then enlarged with crisp edges."""
    n = 128
    field = _fbm(rng, n, n, 4, 3)
    coarse = field.reshape(n // 2, 2, n // 2, 2).mean((1, 3)).repeat(2, 0).repeat(2, 1)
    fine = field + (rng.random((n, n), dtype=np.float32) - 0.5) * 0.14
    fray = rng.random((n, n)) < 0.5
    value = np.where(fray, fine, coarse)
    index = np.clip((value * len(colours)).astype(np.int32), 0, len(colours) - 1)
    small = np.stack([_rgb(c) for c in colours])[index]
    # A faint per-block tint so the blocks read as printed squares.
    small = small * (0.97 + 0.06 * rng.random((n, n, 1), dtype=np.float32))
    return _blocks(small, h, w)


def _shape_stripe(rng, h, w, colours, luma):
    """Tiger stripes as brush strokes: warped waves for the strokes, a dry-brush fringe of bristle streaks along each
    edge, and thinner secondary flicks here and there."""
    u, v = _uv(h, w)
    warp = _fbm(rng, h, w, 3, 4)
    width = 0.35 + 0.4 * _fbm(rng, h, w, 4, 3)
    s = np.sin(u * _TAU * 7 + warp * 9.0 + v * 2.0) - (1.0 - width)
    streak = 0.6 * _noise(rng, h, w, 6, 260) + 0.4 * _noise(rng, h, w, 14, 620)
    near = np.exp(-(s / 0.3) ** 2)
    s = s + (streak - 0.5) * 1.1 * near
    flick = np.sin(u * _TAU * 16 + warp * 14.0 + v * 3.0 + 1.3) - 0.93 + (_fbm(rng, h, w, 5, 2) - 0.72) * 0.9
    flick = flick + (streak - 0.5) * 0.35
    a = _edge(np.maximum(s, flick))
    dark = _rgb(colours[0]) * (0.9 + 0.25 * streak)[..., None]
    return _mix(np.broadcast_to(_rgb(colours[1]), (h, w, 3)) * (0.95 + 0.1 * streak)[..., None], dark, a)


def _engraving(rng, h, w, cogs: bool):
    """Fine engraving for the gold finishes: (groove, raised) h x w 0..1. Small spiral scrolls with leaves scattered
    over engine-turned waves; with cogs, engraved cog outlines too (the scrolls fill the gaps)."""
    cv = _Canvas(h, w)
    u, v = _uv(h, w)
    px = cv.px
    groove = np.zeros((h, w), np.float32)
    raised = np.zeros((h, w), np.float32)
    clear = np.zeros((h, w), np.float32)
    # Engine-turned waves: fine parallel wavy lines.
    f = (v + 0.25 * u) * 150.0 + 3.0 * _noise(rng, h, w, 5) + 0.8 * np.sin(u * _TAU * 9 + v * _TAU * 3)
    gy, gx = np.gradient(f)
    dist = np.abs(((f + 0.5) % 1.0) - 0.5) / (np.hypot(gx, gy) + 1e-6) * px
    waves = 0.45 * _ink(dist, 0.00028, px)
    if cogs:
        for x, y, r, ang, teeth, extra in _cog_layout(rng, 40, 5, 0.010, 0.026, 0.0042):
            iy, ix, X, Y = cv.tile(x, y, r * 1.15)
            lx, ly = _rot(X, Y, ang)
            lx, ly = lx / r, ly / r
            m = 0.0042 / np.pi
            d = _sd_gear(lx, ly, teeth, (r - 2.25 * m) / r, (r - m) / r, 0.00105 / r) * r
            rr = np.hypot(lx, ly)
            lines = np.maximum(_ink(np.abs(d), 0.00034, px), _ink(np.abs(rr - 0.32) * r, 0.0003, px))
            lines = np.maximum(lines, _ink(np.abs(rr - 0.12) * r, 0.0003, px))
            spokes = 3 + int(extra * 3)
            fx, fy = _fold(lx, ly, spokes)
            hole = np.maximum(np.maximum(rr - 0.62, 0.40 - rr), -(np.abs(fy) - 0.09))
            lines = np.maximum(lines, _ink(np.abs(hole) * r, 0.0003, px))
            groove[iy, ix] = np.maximum(groove[iy, ix], lines)
            inside = _cov(d, px)
            raised[iy, ix] = np.maximum(raised[iy, ix], 0.35 * inside * _cov(-hole * r, px))
            clear[iy, ix] = np.maximum(clear[iy, ix], inside)
    # Scrolls: spirals with two leaves off their outer end.
    xs, ys, rs, angs, extras = _scatter(rng, 1600 if not cogs else 900, 0.007, 0.014, gap=1.05)
    for x, y, r, ang, ex in zip(xs, ys, rs, angs, extras):
        if cogs and _cog_hit(cv, clear, x, y, r):
            continue
        turns = 1.5 + ex * 1.0
        iy, ix, X, Y = cv.tile(x, y, r * 2.1)
        lx, ly = _rot(X, Y, ang)
        lx, ly = lx / r, ly / r
        if ex > 0.5:
            ly = -ly
        d = _sd_spiral(lx, ly, turns)
        end = _TAU * turns
        ex_, ey_ = np.cos(end), np.sin(end)
        tx, ty = -np.sin(end), np.cos(end)  # the spiral's direction at its outer end
        l1, v1 = _sd_leaf(lx, ly, ex_, ey_, ex_ + 1.0 * (tx * 0.8 + ex_ * 0.6), ey_ + 1.0 * (ty * 0.8 + ey_ * 0.6), 0.22)
        l2, v2 = _sd_leaf(lx, ly, ex_, ey_, ex_ + 0.7 * (tx * 0.6 - ex_ * 0.8), ey_ + 0.7 * (ty * 0.6 - ey_ * 0.8), 0.16)
        lines = np.maximum(_ink(d * r, 0.00034, px), _ink(np.abs(np.minimum(l1, l2)) * r, 0.00028, px))
        lines = np.maximum(lines, 0.6 * _ink(np.minimum(v1, v2) * r, 0.0002, px) * _cov(np.minimum(l1, l2) * r, px))
        groove[iy, ix] = np.maximum(groove[iy, ix], lines)
        raised[iy, ix] = np.maximum(raised[iy, ix], 0.5 * _cov(np.minimum(l1, l2) * r, px))
        clear[iy, ix] = np.maximum(clear[iy, ix], _cov((np.hypot(lx, ly) - 1.25) * r, px))
    groove = np.maximum(groove, waves * (1.0 - clear))
    # A burnished edge on the lit side of each groove.
    k = max(1, int(round(0.0005 / px)))
    burnish = np.clip(np.roll(groove, (k, k), (0, 1)) - groove, 0.0, 1.0)
    return groove, np.maximum(raised, 0.8 * burnish)


def _cog_hit(cv, clear, x, y, r) -> bool:
    """Whether a spot is already mostly taken (engraved cogs)."""
    iy, ix, X, Y = cv.tile(x, y, r * 0.5)
    return bool(clear[iy, ix].mean() > 0.05)


def _gold_ramp(luma, colours, groove=None, raised=None):
    """The retail brightness stretched over the whole gold ramp (gun textures are dark: unstretched, most of the gun
    lands on the darkest gold), grooves cut darker and raised work burnished lighter. A flat luma (the menu swatch)
    is taken as is."""
    lo, hi = np.percentile(luma, 2), np.percentile(luma, 98)
    stretched = luma if hi - lo < 0.02 else np.clip((luma - lo) / max(float(hi - lo), 1e-3), 0.0, 1.0)
    t = 0.25 + 0.75 * stretched
    if groove is None:
        return _ramp(t, colours)
    t = t - 0.34 * groove + 0.16 * raised * (1.0 - groove)
    return _ramp(t, colours) * (1.0 - 0.18 * groove)[..., None]


def _shape_gold(rng, h, w, colours, luma):
    """Gold following the retail brightness, with fine engraved scrollwork."""
    groove, raised = _engraving(rng, h, w, cogs=False)
    return _gold_ramp(luma, colours, groove, raised)


def _shape_mastercraft(rng, h, w, colours, luma):
    """Gold like "golden" with fine engraved cogs and filigree scrolls in the gaps."""
    groove, raised = _engraving(rng, h, w, cogs=True)
    return _gold_ramp(luma, colours, groove, raised)


def _shape_flames(rng, h, w, colours, luma):
    """Black with rows of flame tongues licking up (colours: the black, then the flame ramp from its dark red edge to
    its pale core), with fine tongues on top and sparks rising off them. The texture's UV islands are not known, so the
    rows repeat down the texture and each island takes a slice of flames."""
    u, v = _uv(h, w)
    rows = 3
    sway = _noise(rng, h, w, 3, 6) - 0.5
    tall = 0.5 * _noise(rng, h, w, 4 * rows, 22) + 0.3 * _noise(rng, h, w, 8 * rows, 44) \
        + 0.2 * _noise(rng, h, w, 16 * rows, 96)
    tall = _warp(tall, sway * 0.06, 0.0)
    lick = _noise(rng, h, w, 30 * rows, 190)
    # 0 at the top of a row, 1 at its foot: the fire is hottest at the foot and thins out upward.
    foot = (v * rows) % 1.0
    heat = foot ** 2.4 * 1.3 - (1.0 - tall) * 1.15 + (lick - 0.5) * 0.14
    edge = 0.05
    flame = np.clip((heat - edge) / 0.85, 0.0, 1.0)
    fire = _ramp(flame, colours[1:])
    # A soft dark-red fringe where a tongue meets the black, then black.
    cover = _edge(heat - edge + 0.02, soft=0.03)
    cv = _Canvas(h, w, _mix(np.broadcast_to(_rgb(colours[0]), (h, w, 3)), fire, cover))
    for x, y, r, s in rng.random((260, 4)):
        _glow(cv, x * cv.W, y * cv.H, 0.0006 + 0.0012 * r, _rgb(colours[4]), 0.5 + 0.5 * s, 1.5 + 2.0 * s,
              -np.pi / 2 + (r - 0.5) * 0.6)
    return cv.rgb


def _shape_livery(rng, h, w, colours, luma):
    """Japanese racing livery (colours: the red, the navy, the white, the yellow, the black): a red body with navy
    panels edged in white, white racing stripes, yellow and black hazard strips, and white decal plates of
    katakana-like strokes."""
    red, navy, white, yellow, black = (_rgb(c) for c in colours)
    u, v = _uv(h, w)
    L = max(h, w)
    out = np.empty((h, w, 3), np.float32)
    out[:] = red
    # Navy panels: slanted bands, a thin white line along each edge.
    a = (v * 3.0 + u * 0.8) % 1.0
    fa = np.hypot(3.0, 0.8) / L
    out = _mix(out, white, _span(a, 0.55, 0.89, fa))
    out = _mix(out, navy, _span(a, 0.58, 0.86, fa))
    # Twin white racing stripes across the red.
    b = (u * 2.0 - v * 1.2) % 1.0
    fb = np.hypot(2.0, 1.2) / L
    stripes = np.maximum(_span(b, 0.30, 0.36, fb), _span(b, 0.40, 0.43, fb)) * (1.0 - _span(a, 0.55, 0.89, fa))
    out = _mix(out, white, stripes)
    # Hazard strips just under each navy panel, outlined in black.
    hz = _span(a, 0.90, 0.95, fa)
    hatch = _stripe((u + v) / 0.025, 0.5, np.hypot(1, 1) / 0.025 / L)
    out = _mix(out, black, _span(a, 0.895, 0.955, fa))
    out = _mix(out, _mix(black, yellow, hatch), hz)
    # Decal plates: white plates carrying rows of katakana-like strokes in navy, and glyph rows straight on the red.
    cv = _Canvas(h, w, out)
    cell = 1.0 / 40
    for _ in range(16):
        pw, ph = cell * (4 + rng.integers(0, 6)), cell * (1.5 + rng.random() * 1.5)
        x0, y0 = rng.random() * (cv.W - pw), rng.random() * (cv.H - ph)
        plate = rng.random() < 0.5
        ink = navy if plate else white
        cx, cy = x0 + pw / 2, y0 + ph / 2
        iy, ix, X, Y = cv.tile(cx, cy, max(pw, ph) / 2 + cell)
        if plate:
            cv.over(iy, ix, _cov(_sd_box(X, Y, pw / 2, ph / 2, cell * 0.12), cv.px), white)
            cv.over(iy, ix, _ink(np.abs(_sd_box(X, Y, pw / 2 - cell * 0.18, ph / 2 - cell * 0.18, cell * 0.06)),
                                 cell * 0.025, cv.px), navy)
        d = np.full(X.shape, 1.0, np.float32)
        t = cell * 0.07
        gx = x0 + cell / 2
        while gx + cell <= x0 + pw - cell / 2:
            gy = y0 + (ph - cell) / 2
            for _s in range(2 + rng.integers(0, 2)):
                kind = rng.integers(0, 4)
                k = rng.random()
                if kind == 0:  # a horizontal bar
                    seg = (gx, gy + t + k * (cell - 2 * t), gx + cell, gy + t + k * (cell - 2 * t))
                elif kind == 1:  # a vertical bar
                    seg = (gx + t + k * (cell - 2 * t), gy, gx + t + k * (cell - 2 * t), gy + cell)
                elif kind == 2:  # a slanted stroke
                    seg = (gx + cell - t, gy + t, gx + cell * 0.45 * k, gy + cell - t)
                else:  # a short tick
                    seg = (gx + cell * (0.2 + 0.5 * k), gy + cell * 0.15, gx + cell * (0.35 + 0.5 * k), gy + cell * 0.45)
                d = np.minimum(d, _sd_segment(X, Y, seg[0] - cx, seg[1] - cy, seg[2] - cx, seg[3] - cy))
            gx += cell + cell / 3
        cv.over(iy, ix, _ink(d, t, cv.px), ink)
    return cv.rgb


def _shape_cryo(rng, h, w, colours, luma):
    """Clean sci-fi panels (colours: the white, the black, the deep blue, the sky blue): diagonal bands cycling white
    panel, black panel with white slanted ribs, and a deep-to-sky blue gradient strip with a white pinstripe, split by
    black seams. Panel cuts cross each band, rivets line the seams and small stencil marks sit on the panels."""
    white, black, deep, sky = (_rgb(c) for c in colours)
    u, v = _uv(h, w)
    L = max(h, w)
    px = 1.0 / L
    s5 = np.sqrt(1.25)
    a = (u + 0.5 * v) * 4.5
    band = np.floor(a).astype(np.int32)
    f = a - band
    kind = band % 3
    bw = 1.0 / (4.5 * s5)  # band width, texture units
    fa = 4.5 * s5 / L
    q = (-0.5 * u + v) / s5  # along the bands, texture units
    ribs = _stripe((u * 0.7 - v) * 16.0, 0.35, 16.0 * np.hypot(0.7, 1.0) / L) * _span(f, 0.18, 0.82, fa)
    black_panel = _mix(black, white, ribs)
    blue_panel = _mix(_mix(deep, sky, f), white, _span(f, 0.585, 0.655, fa))
    out = np.where((kind == 0)[..., None], white, np.where((kind == 1)[..., None], black_panel, blue_panel))
    # Panel cuts across each band at its own spacing, a light line beside each.
    table = rng.random((64, 2)).astype(np.float32)
    length = 0.07 + 0.09 * table[band % 64, 1]
    cq = (q / length + table[band % 64, 0]) % 1.0
    dc = np.minimum(cq, 1.0 - cq) * length
    inner = _span(f, 0.04, 0.96, fa)
    out = _mix(out, black, 0.85 * _ink(dc, 0.0007, px) * inner)
    out = _mix(out, white, 0.25 * _ink(np.abs(cq * length - 0.0017), 0.0004, px) * inner)
    # Seams between the bands, with a light lip on the lit side.
    out = _mix(out, white, 0.3 * _span(f, 0.03, 0.05, fa))
    out = _mix(out, black, 1.0 - _span(f, 0.03, 0.97, fa))
    # Rivets along both edges of every band.
    sp = 0.016
    rq = (q % sp) - sp / 2
    for edge_at in (0.012, bw - 0.012):
        dp = f * bw - edge_at
        # Back to x / y for the lighting: the across axis is (1, 0.5) / s5, the along axis (-0.5, 1) / s5.
        dx, dy = (dp - 0.5 * rq) / s5, (0.5 * dp + rq) / s5
        cov, shade, spec = _dome(dx, dy, 0.0028, px)
        steel = _mix(white, black, 0.45) * shade[..., None] + 0.4 * spec[..., None]
        out = _mix(out, black, 0.4 * _cov(np.hypot(dx - 0.0008, dy - 0.001) - 0.0031, px))
        out = _mix(out, np.clip(steel, 0, 1), cov)
    # Stencil marks: chevron rows, bars of stencil "text" and small boxed marks, black on white, white elsewhere.
    cv = _Canvas(h, w, out)
    along = np.arctan2(1.0, -0.5)
    for x, y, kind_r, k1, k2 in rng.random((60, 5)):
        x, y = x * cv.W, y * cv.H
        fb = (x + 0.5 * y) * 4.5
        if not 0.15 < fb % 1.0 < 0.85:
            continue
        ink = black if int(np.floor(fb)) % 3 == 0 else white
        size = 0.006 + 0.006 * k1
        iy, ix, X, Y = cv.tile(x, y, size * 3.2)
        lx, ly = _rot(X, Y, along + (np.pi if k2 < 0.5 else 0.0))
        lx, ly = lx / size, ly / size
        if kind_r < 0.35:
            d = np.minimum(np.minimum(_sd_chevron(lx + 1.1, ly), _sd_chevron(lx, ly)), _sd_chevron(lx - 1.1, ly))
        elif kind_r < 0.7:
            d = np.full(lx.shape, 1.0, np.float32)
            xpos = -2.8
            for j in range(int(4 + k2 * 4)):
                wseg = 0.25 + 0.5 * ((k1 * 7.3 + j * 0.37) % 1.0)
                d = np.minimum(d, _sd_box(lx - xpos - wseg, ly, wseg, 0.22))
                xpos += 2 * wseg + 0.25
            d = np.minimum(d, _sd_box(lx, ly + 0.6, 2.8, 0.06))
        else:
            d = np.minimum(np.abs(_sd_box(lx, ly, 1.4, 0.9, 0.15)) - 0.1, _sd_box(lx, ly, 0.8, 0.15))
        cv.over(iy, ix, 0.85 * _cov(d * size, cv.px), ink)
    return cv.rgb


def _shape_scanlines(rng, h, w, colours, luma):
    """A laser grid: glowing lines (a hot core with a two-step falloff) across the texture every 1/32, fainter uprights
    every 1/8, brighter nodes where they cross and a faint fine grid behind; each line flickers a little."""
    base, red = _rgb(colours[0]), _rgb(colours[1])
    u, v = _uv(h, w)
    px = 1.0 / max(h, w)
    row = np.floor(v * 32 - 0.125 + 0.5).astype(np.int32)
    dh = np.abs(((v * 32 - 0.125 + 0.5) % 1.0) - 0.5) / 32
    dv = np.abs(((u * 8 + 0.5) % 1.0) - 0.5) / 8
    flicker = rng.random(64).astype(np.float32)[row % 64] * 0.3 + 0.7
    flicker = flicker * (0.85 + 0.3 * _noise(rng, h, w, 4, 64))
    fine = np.minimum(np.abs(((u * 128 + 0.5) % 1.0) - 0.5), np.abs(((v * 128 + 0.5) % 1.0) - 0.5)) / 128

    def beam(d, hw):
        return _ink(d, hw, px), 0.55 * np.exp(-d / 0.0032) + 0.2 * np.exp(-d / 0.011)

    ch, gh = beam(dh, 0.0009)
    cvv, gv = beam(dv, 0.0007)
    node = np.exp(-np.hypot(dh, dv) / 0.004)
    glow = np.clip(gh * flicker + 0.7 * gv + 0.6 * node + 0.12 * _ink(fine, 0.0003, px), 0.0, 1.0)
    core = np.clip(ch * flicker + 0.8 * cvv, 0.0, 1.0)
    out = _mix(np.broadcast_to(base, (h, w, 3)) * (0.9 + 0.2 * _noise(rng, h, w, 8))[..., None], red, glow)
    out = _mix(out, red, core)
    return _mix(out, np.array([1.0, 0.82, 0.78], np.float32), 0.35 * core * np.clip(ch + node, 0, 1))


def _shape_noise(rng, h, w, colours, luma):
    """TV static: crisp snow on two grain sizes (1/384 and 1/768 of the texture) with torn rows, slow rolling bright
    bars and faint scanlines."""
    u, v = _uv(h, w)
    L = max(h, w)

    def snow(n):
        g = rng.random((n, n), dtype=np.float32)
        shift = (rng.random(n) < 0.15) * rng.integers(0, n, n)
        g = g[np.arange(n)[:, None], (np.arange(n)[None, :] + shift[:, None]) % n]
        return _blocks(g[..., None], h, w)[..., 0]

    value = 0.35 * snow(384) + 0.25 * snow(768) + 0.4 * _fbm(rng, h, w, 8, 2)
    out = _bands(value, colours)
    # Rolling bars every seventh row of 1/64, each its own brightness, edges soft.
    k = np.floor(v * 64).astype(np.int32)
    level = rng.random(64).astype(np.float32)[k % 64] * 0.4 + 0.6
    bar = _span((v * 64) % 7.0, 0.0, 1.0, 64.0 / L + 0.1) * level
    out = _mix(out, _rgb(colours[-1]) * (0.85 + 0.15 * _noise(rng, h, w, 32, 384))[..., None], bar)
    return out * (1.0 - 0.12 * _stripe(v * 256, 0.3, 256.0 / L))[..., None]


def _shape_arcs(rng, h, w, colours, luma):
    """Shockwave: branching lightning arcs radiating from small bright nodes over a dark blue haze."""
    dark, glow_c, core_c = (_rgb(c) for c in colours)
    cv = _Canvas(h, w)
    haze = _fbm(rng, h, w, 3, 4)
    cv.rgb = _mix(np.broadcast_to(dark, (h, w, 3)), glow_c * 0.6, haze ** 2 * 0.25)
    field = np.full((h, w), 1.0, np.float32)
    segs: list = []
    xs, ys, rs, angs, extras = _scatter(rng, 200, 0.02, 0.03, gap=1.0)
    for x, y, ang, ex in zip(xs, ys, angs, extras):
        for k in range(3 + int(ex * 3)):
            a = ang + k * _TAU / (3 + int(ex * 3)) + rng.normal(0, 0.3)
            ln = 0.035 + 0.05 * rng.random()
            _lightning(rng, x, y, x + ln * np.cos(a), y + ln * np.sin(a), 0.0011, 5, 0.4, out=segs)
    _segments(cv, field, segs, 0.009)
    core = _cov(field, cv.px)
    near = np.exp(-np.maximum(field, 0) / 0.0025)
    wide = _blur(core, 0.008, cv.px)
    rgb = cv.rgb
    rgb += (glow_c * np.clip(wide * 2.5, 0, 0.6)[..., None])
    rgb = _mix(rgb, glow_c, 0.75 * near)
    rgb = _mix(np.clip(rgb, 0, 1), core_c, core)
    cv.rgb = rgb
    for x, y in zip(xs, ys):
        _glow(cv, x, y, 0.0022, core_c, 1.0)
    return cv.rgb


def _shape_cracks(rng, h, w, colours, luma):
    """Broken concrete: a mottled base with fine aggregate, branching cracks with chipped, flaked edges and a dark
    core, and small spalled pits."""
    cv = _Canvas(h, w)
    base = _bands(0.5 + 0.5 * (_fbm(rng, h, w, 3, 3) - 0.5), colours[1:], soft=0.15)
    agg = _noise(rng, h, w, 500)
    base = _mix(base, _rgb(colours[0]), 0.35 * _ss(0.3, 0.18, agg))
    base = _mix(base, _rgb(colours[2]) * 1.08, 0.3 * _ss(0.72, 0.84, agg))
    cv.rgb = base.astype(np.float32)
    field = np.full((h, w), 1.0, np.float32)
    segs: list = []
    for x, y, a, n in rng.random((90, 4)):
        _crack(rng, x * cv.W, y * cv.H, a * _TAU, 10 + int(n * 12), 0.011, 0.0022, 2, segs)
    _segments(cv, field, segs, 0.008)
    rough = _noise(rng, h, w, 260) * 0.7 + _noise(rng, h, w, 700) * 0.3
    chip = _edge(0.0045 * (rough * 1.7 - 0.45) - np.maximum(field, 0))
    out = _mix(cv.rgb, _rgb(colours[2]) * 1.1, 0.75 * chip)
    out = _mix(out, _rgb(colours[0]) * 0.8, 0.5 * _ink(np.abs(0.0045 * (rough * 1.7 - 0.45) - np.maximum(field, 0)),
                                                       0.0003, cv.px) * (field < 0.006))
    out = _mix(out, _rgb(colours[0]) * 0.6, _cov(field, cv.px))
    cv.rgb = out
    # Spalled pits: small irregular flakes knocked out of the surface.
    xs, ys, rs, angs, extras = _scatter(rng, 400, 0.0025, 0.007, gap=1.4)
    for x, y, r, ang, ex in zip(xs, ys, rs, angs, extras):
        n = 5 + int(ex * 3)
        pts = [((0.6 + 0.4 * ((ex * 13.1 + i * 0.618) % 1.0)) * np.cos(i * _TAU / n),
                (0.6 + 0.4 * ((ex * 7.7 + i * 0.414) % 1.0)) * np.sin(i * _TAU / n)) for i in range(n)]
        iy, ix, X, Y = cv.tile(x, y, r * 1.3)
        lx, ly = _rot(X, Y, ang)
        d = _sd_polygon(lx / r, ly / r, pts) * r
        _relief(cv, iy, ix, d, _rgb(colours[0]) * 1.3, r * 0.35, -0.6)
    return cv.rgb


def _shape_shrapnel(rng, h, w, colours, luma):
    """Frag: dark scorched blotches with scattered metal shards (bevelled, a hot orange rim on their lit edges) and
    sparks."""
    cv = _Canvas(h, w)
    cv.rgb = _bands(_fbm(rng, h, w, 4, 4) * 0.99, colours[:2]) * (0.92 + 0.16 * _noise(rng, h, w, 90))[..., None]
    hot = _rgb(colours[2])
    metal = _mix(_rgb(colours[1]), hot, np.float32(0.12)) * 1.2
    xs, ys, rs, angs, extras = _scatter(rng, 900, 0.004, 0.014, gap=0.8)
    for x, y, r, ang, ex in zip(xs, ys, rs, angs, extras):
        n = 3 + int(ex * 3)
        stretch = 1.0 + 1.6 * ((ex * 5.3) % 1.0)
        pts = [((0.55 + 0.45 * ((ex * 11.3 + i * 0.618) % 1.0)) * np.cos(i * _TAU / n + 0.4 * ((ex * 3.1 + i) % 1.0))
                * stretch, (0.55 + 0.45 * ((ex * 6.1 + i * 0.414) % 1.0)) * np.sin(i * _TAU / n)) for i in range(n)]
        iy, ix, X, Y = cv.tile(x, y, r * stretch * 1.3)
        lx, ly = _rot(X, Y, ang)
        sx, sy = _rot(X - r * 0.12, Y - r * 0.16, ang)
        _shadow(cv, iy, ix, _sd_polygon(sx / r, sy / r, pts) * r, r * 0.2, 0.6)
        d = _sd_polygon(lx / r, ly / r, pts) * r
        _relief(cv, iy, ix, d, metal, r * 0.3, 0.8, gloss=0.1)
        gy, gx = np.gradient(d, cv.px)
        lit = np.clip((gx * _LIGHT[0] + gy * _LIGHT[1]) / (np.hypot(gx, gy) + 1e-6), 0, 1)
        cv.over(iy, ix, _cov(d, cv.px) * np.clip(1 + d / (r * 0.14), 0, 1) * lit * 0.9, hot)
    for x, y, r, s in rng.random((500, 4)):
        _glow(cv, x * cv.W, y * cv.H, 0.0005 + 0.0009 * r, hot, 0.6 + 0.4 * s, 1.0 + 3.0 * s * s, s * _TAU)
    return cv.rgb


def _shape_hazard(rng, h, w, colours, luma):
    """Lockdown: bold yellow and black hazard stripes, the paint chipped and worn (more at the stripe edges) down to
    bare metal, chip edges darker, grime streaks running down."""
    black, yellow = _rgb(colours[0]), _rgb(colours[1])
    u, v = _uv(h, w)
    L = max(h, w)
    px = 1.0 / L
    s = (u + v) * 4.0
    paint = _mix(np.broadcast_to(black, (h, w, 3)), yellow, _stripe(s, 0.5, 4.0 * np.sqrt(2.0) / L))
    e = (s * 2.0) % 1.0
    de = np.minimum(e, 1.0 - e) / (8.0 * np.sqrt(2.0))  # distance to a stripe edge, texture units
    wear = 0.5 * _noise(rng, h, w, 24) + 0.3 * _noise(rng, h, w, 64) + 0.2 * _noise(rng, h, w, 170)
    chip_f = wear + 0.3 * np.exp(-de / 0.004) - 0.80
    chip = _edge(chip_f)
    metal = _mix(black, np.array([0.55, 0.55, 0.56], np.float32), 0.55 * (0.8 + 0.4 * _noise(rng, h, w, 300)))
    out = _mix(paint, metal, chip)
    out = _mix(out, black, 0.35 * _edge(chip_f + 0.02) * (1.0 - chip))
    grime = _noise(rng, h, w, 4, 140) * _noise(rng, h, w, 12)
    out = out * (1.0 - 0.25 * _ss(0.35, 0.75, grime))[..., None]
    # Scratches through the paint to the metal.
    cv = _Canvas(h, w, out)
    field = np.full((h, w), 1.0, np.float32)
    segs = []
    for x, y, a, ln in rng.random((70, 4)):
        x, y, a = x * cv.W, y * cv.H, a * _TAU
        segs.append((x, y, x + (0.01 + 0.03 * ln) * np.cos(a), y + (0.01 + 0.03 * ln) * np.sin(a), 0.0))
    _segments(cv, field, segs, 2 * px)
    return _mix(cv.rgb, metal, 0.8 * _ink(np.maximum(field, 0), 0.00035, px))


def _shape_smoke(rng, h, w, colours, luma):
    """Billowing smoke: value noise warped twice through itself (curls and wisps), a smooth ramp through the greys and
    thin pale filaments along its folds."""
    a, b = _fbm(rng, h, w, 3, 4) - 0.5, _fbm(rng, h, w, 3, 4) - 0.5
    c, d = _fbm(rng, h, w, 4, 4), _fbm(rng, h, w, 4, 4)
    rx = _warp(c, a * 0.22, b * 0.22) - 0.5
    ry = _warp(d, b * 0.22, -a * 0.22) - 0.5
    e = _fbm(rng, h, w, 3, 6)
    density = _norm(_warp(e, rx * 0.18, ry * 0.18) + 0.08 * (_noise(rng, h, w, 96) - 0.5))
    out = _ramp(density ** 1.15, colours)
    wisps = np.exp(-((density - 0.62) / 0.018) ** 2) + 0.6 * np.exp(-((density - 0.42) / 0.014) ** 2)
    out = _mix(out, _rgb(colours[3]), 0.35 * np.clip(wisps, 0, 1) * (0.5 + 0.5 * _noise(rng, h, w, 20)))
    return out


def _shape_chaff(rng, h, w, colours, luma):
    """Chaff: a dark ground with glinting foil strips scattered at random angles, each crumpled into two tones along
    its length, some with a star glint."""
    dark, mid, light = (_rgb(c) for c in colours)
    cv = _Canvas(h, w)
    cv.rgb = _mix(np.broadcast_to(dark, (h, w, 3)), mid, 0.16 * _fbm(rng, h, w, 4, 4))
    for x, y, ang, ln, wd, tone, kink, dt, glint in rng.random((1100, 9)):
        x, y, ang = x * cv.W, y * cv.H, ang * np.pi
        half, hw = (0.012 + 0.024 * ln) / 2, 0.0006 + 0.0007 * wd
        iy, ix, X, Y = cv.tile(x, y, half + hw + 0.002)
        lx, ly = _rot(X, Y, ang)
        sx, sy = _rot(X - 0.0006, Y - 0.0008, ang)
        _shadow(cv, iy, ix, _sd_box(sx, sy, half, hw), 0.0008, 0.5)
        t = tone + np.where(lx / half > kink * 1.6 - 0.8, dt - 0.5, 0.0)
        t = np.clip(t + 0.15 * np.sin(lx / half * 4.0 + tone * 9), 0.0, 1.0)
        col = _mix(mid, light, t * t)
        cv.over(iy, ix, _cov(_sd_box(lx, ly, half, hw), cv.px), col)
        if glint > 0.86:
            gx = (kink - 0.5) * half
            gxw, gyw = _rot(np.float32(gx), np.float32(0.0), -ang)
            jy, jx, GX, GY = cv.tile(x + gxw, y + gyw, 0.006)
            star = np.exp(-np.abs(GX) / 0.0004) * np.exp(-np.abs(GY) / 0.004) \
                + np.exp(-np.abs(GY) / 0.0004) * np.exp(-np.abs(GX) / 0.004) \
                + np.exp(-np.hypot(GX, GY) / 0.0008)
            cv.add(jy, jx, np.clip(star, 0, 1) * 0.9, light)
    return cv.rgb


def _shape_turnabout(rng, h, w, colours, luma):
    """Turnabout: scorched ground pocked with small blast craters, cracks radiating from each, ember light glowing in
    the cracks and on the crater floors."""
    dark, char, ember = (_rgb(c) for c in colours)
    pale = np.array([1.0, 0.86, 0.5], np.float32)
    cv = _Canvas(h, w)
    ground = _fbm(rng, h, w, 3, 5)
    cv.rgb = _mix(np.broadcast_to(dark, (h, w, 3)), char, 0.55 * ground ** 1.5) \
        * (0.9 + 0.2 * _noise(rng, h, w, 400))[..., None]
    field = np.full((h, w), 1.0, np.float32)
    segs: list = []
    xs, ys, rs, angs, extras = _scatter(rng, 260, 0.005, 0.014, gap=2.4)
    for x, y, r, ang, ex in zip(xs, ys, rs, angs, extras):
        # Soot around the crater.
        iy, ix, X, Y = cv.tile(x, y, r * 3.5)
        d = np.hypot(X, Y) / r
        cv.darken(iy, ix, 0.7 * np.exp(-np.maximum(d - 1.0, 0) / 0.9) * (d > 0.6))
        n = 5 + int(ex * 5)
        for k in range(n):
            a = ang + k * _TAU / n + rng.normal(0, 0.25)
            ln = r * (2.0 + 3.5 * rng.random())
            _lightning(rng, x + r * 0.9 * np.cos(a), y + r * 0.9 * np.sin(a), x + (r + ln) * np.cos(a),
                       y + (r + ln) * np.sin(a), 0.0011, 4, 0.5, 0.18, segs)
    _segments(cv, field, segs, 0.006)
    core = _cov(field, cv.px)
    rgb = cv.rgb + ember * np.clip(_blur(core, 0.004, cv.px) * 1.6, 0, 0.5)[..., None]
    rgb = _mix(rgb, ember * 0.8, 0.8 * np.exp(-np.maximum(field, 0) / 0.0016))
    rgb = _mix(rgb, ember, core)
    cv.rgb = _mix(rgb, pale, core * _ss(0.0, -0.0008, field))
    for x, y, r, ang, ex in zip(xs, ys, rs, angs, extras):
        iy, ix, X, Y = cv.tile(x, y, r * 1.3)
        rr = np.hypot(X, Y) / r
        rim = np.maximum(rr - 1.0, 0.72 - rr) * r
        _relief(cv, iy, ix, (rr - 0.85) * r, char * 0.55, r * 0.4, -0.7)  # the bowl, lit on its far side
        _relief(cv, iy, ix, rim, char * 1.2, r * 0.12, 0.7)
        cv.add(iy, ix, 0.8 * np.exp(-(rr / 0.35) ** 2) * (0.6 + 0.4 * ex), ember)
    return cv.rgb


def _shape_minefield(rng, h, w, colours, luma):
    """Minefield: olive armour plate in riveted panels, small hazard-striped marker plates and stencilled mine
    silhouettes (round, with prongs), the stencil paint worn."""
    olive_d, olive, yellow = (_rgb(c) for c in colours)
    u, v = _uv(h, w)
    L = max(h, w)
    px = 1.0 / L
    grime = _fbm(rng, h, w, 4, 5)
    out = _mix(np.broadcast_to(olive, (h, w, 3)), olive_d, 0.6 * grime ** 1.6)
    out = out * (0.94 + 0.12 * _noise(rng, h, w, 4, 200))[..., None]
    # Panels: rows of 1/8 offset like brickwork, each panel 1/4 wide; a dark seam and a lit lip under each.
    row = np.floor(v * 8).astype(np.int32)
    offs = rng.random(8).astype(np.float32)[row % 8]
    su = ((u * 4 + offs) % 1.0) / 4
    sv = (v * 8 % 1.0) / 8
    seam = np.minimum(np.minimum(su, 0.25 - su), np.minimum(sv, 0.125 - sv))
    out = _mix(out, olive_d * 0.6, _ink(seam, 0.0008, px))
    out = _mix(out, olive * 1.3, 0.35 * np.maximum(_span(sv, 0.0012, 0.0026, 8.0 / L / 8),
                                                  _span(su, 0.0012, 0.0026, 4.0 / L / 4)))
    # Rivets along the top and left of each panel.
    for dx, dy in (((su * 64 % 1.0 - 0.5) / 64 * 4 / 4, sv - 0.005), (su - 0.005, ((sv * 64) % 1.0 - 0.5) / 64)):
        cov, shade, spec = _dome(dx, dy, 0.0024, px)
        out = _mix(out, olive_d * 0.5, 0.45 * _cov(np.hypot(dx - 0.0007, dy - 0.0009) - 0.0027, px))
        out = _mix(out, np.clip(olive * 1.1 * shade[..., None] + 0.3 * spec[..., None], 0, 1), cov)
    cv = _Canvas(h, w, out.astype(np.float32))
    # Stencilled mines.
    xs, ys, rs, angs, extras = _scatter(rng, 260, 0.011, 0.02, gap=1.6)
    for x, y, r, ang, ex in zip(xs, ys, rs, angs, extras):
        iy, ix, X, Y = cv.tile(x, y, r * 1.1)
        lx, ly = _rot(X, Y, ang)
        d = _sd_mine(lx / r, ly / r, 3 + int(ex * 4)) * r
        worn = _ss(0.25, 0.45, _value_at(rng, X.shape))
        cv.over(iy, ix, _cov(d, cv.px) * (0.95 - 0.45 * worn), yellow)
        cv.over(iy, ix, 0.08 * _cov(d - r * 0.06, r * 0.08), yellow)  # a little overspray
    # Marker plates: small raised plates with diagonal yellow and black stripes.
    taken = (xs, ys, rs)
    mx, my, mr, mang, mex = _scatter(rng, 160, 0.01, 0.016, gap=1.3, avoid=taken)
    for x, y, r, ang, ex in zip(mx, my, mr, mang, mex):
        iy, ix, X, Y = cv.tile(x, y, r * 1.4)
        lx, ly = _rot(X, Y, ang)
        tri = ex < 0.4
        if tri:
            d = _sd_polygon(lx / r, ly / r, [(0.0, -0.95), (0.95, 0.7), (-0.95, 0.7)]) * r
        else:
            d = _sd_box(lx, ly, r, r * 0.45, r * 0.08)
        _shadow(cv, iy, ix, d - r * 0.05, r * 0.2, 0.5)
        stripes = _stripe((lx + ly) / (r * 0.4), 0.5, np.sqrt(2.0) / (r * 0.4) / L)
        # Stripes inside a black border.
        face = _mix(np.broadcast_to(_rgb(0x101010), X.shape + (3,)), yellow, stripes * _cov(d + 0.14 * r, cv.px))
        _relief(cv, iy, ix, d, face, r * 0.1, 0.6)
    return cv.rgb


def _value_at(rng, shape):
    """Cheap smooth noise for a small tile (stencil wear)."""
    from PIL import Image  # noqa: PLC0415

    g = rng.random((4, 4), dtype=np.float32)
    return np.asarray(Image.fromarray(g).resize((shape[1], shape[0]), Image.BICUBIC), np.float32)


def _shape_voltage(rng, h, w, colours, luma):
    """Voltage: a dark circuit board of 45 degree traces with round pads and vias, a few chips, and lightning jumping
    between pads with a glow."""
    board, copper_d, gold, hot = (_rgb(c) for c in colours)
    cv = _Canvas(h, w)
    cv.rgb = _mix(np.broadcast_to(board, (h, w, 3)), copper_d, 0.5 * _fbm(rng, h, w, 6, 3) * 0.6
                  + 0.2 * _noise(rng, h, w, 4, 600))
    g = 1.0 / 160
    dirs = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)]
    field = np.full((h, w), 1.0, np.float32)
    segs, pads = [], []
    for _ in range(320):
        x, y = rng.integers(0, 160) * g, rng.integers(0, 160) * g
        pads.append((x, y, 0))
        d = int(rng.integers(0, 4)) * 2
        for _leg in range(int(rng.integers(2, 6))):
            n = int(rng.integers(2, 9)) if d % 2 == 0 else int(rng.integers(1, 4))
            nx, ny = x + dirs[d][0] * n * g, y + dirs[d][1] * n * g
            segs.append((x, y, nx, ny, 0.0011))
            x, y = nx, ny
            d = (d + int(rng.choice([-1, 1, -1, 1, -2, 2]))) % 8
        pads.append((x, y, 1 if rng.random() < 0.5 else 0))
    _segments(cv, field, segs, 0.003)
    trace = _mix(copper_d, gold, np.float32(0.45))
    cv.rgb = _mix(cv.rgb, board * 0.5, 0.6 * _cov(field - 0.0006, cv.px))  # a dark solder-mask edge
    cv.rgb = _mix(cv.rgb, trace, _cov(field, cv.px))
    cv.rgb = _mix(cv.rgb, gold * 0.8, 0.4 * _ink(np.abs(field + 0.0005), 0.00025, cv.px))  # a lit centre line
    for x, y, kind in pads:
        iy, ix, X, Y = cv.tile(x, y, 0.004)
        rr = np.hypot(X, Y)
        cv.over(iy, ix, _cov(rr - 0.0024, cv.px), gold)
        if kind:  # a via: a ring round a hole
            cv.over(iy, ix, _cov(rr - 0.0011, cv.px), board * 0.4)
        else:
            cv.add(iy, ix, 0.5 * np.exp(-(np.hypot(X + 0.0008, Y + 0.0008) / 0.0008) ** 2), hot)
    # Chips: dark packages with pin rows.
    for x, y, a, s in rng.random((14, 4)):
        x, y, ang = x * cv.W, y * cv.H, (a < 0.5) * np.pi / 2
        hx, hy = 0.008 + 0.008 * s, 0.005 + 0.003 * s
        iy, ix, X, Y = cv.tile(x, y, max(hx, hy) + 0.004)
        lx, ly = _rot(X, Y, ang)
        pins = _stripe(lx / 0.0025, 0.45, 1.0 / 0.0025 / cv.L) * _span(np.abs(ly), hy, hy + 0.0022, 1.0 / cv.L) \
            * (np.abs(lx) < hx - 0.001)
        cv.over(iy, ix, pins, gold)
        _shadow(cv, iy, ix, _sd_box(lx - 0.0006, ly - 0.0008, hx, hy, 0.0005), 0.0012, 0.6)
        _relief(cv, iy, ix, _sd_box(lx, ly, hx, hy, 0.0005), _rgb(0x1A1A20), 0.001, 0.5)
        cv.over(iy, ix, 0.5 * _cov(np.hypot(lx + hx * 0.7, ly + hy * 0.5) - 0.0009, cv.px), _rgb(0x2E2E36))
    # Lightning jumping between pads.
    bolts: list = []
    pxs = np.array([p[0] for p in pads])
    pys = np.array([p[1] for p in pads])
    for i in rng.integers(0, len(pads), 26):
        dd = np.hypot((pxs - pxs[i] + 0.5) % 1.0 - 0.5, (pys - pys[i] + 0.5) % 1.0 - 0.5)
        ok = np.nonzero((dd > 0.03) & (dd < 0.09))[0]
        if not len(ok):
            continue
        j = ok[int(rng.integers(0, len(ok)))]
        tx = pxs[i] + ((pxs[j] - pxs[i] + 0.5) % 1.0 - 0.5)
        ty = pys[i] + ((pys[j] - pys[i] + 0.5) % 1.0 - 0.5)
        _lightning(rng, pxs[i], pys[i], tx, ty, 0.0010, 5, 0.45, out=bolts)
    lf = np.full((h, w), 1.0, np.float32)
    _segments(cv, lf, bolts, 0.009)
    core = _cov(lf, cv.px)
    rgb = cv.rgb + gold * np.clip(_blur(core, 0.007, cv.px) * 2.2, 0, 0.45)[..., None]
    rgb = _mix(rgb, gold, 0.85 * np.exp(-np.maximum(lf, 0) / 0.0022))
    return np.clip(_mix(rgb, hot, core), 0, 1)


def _shape_cogs(rng, h, w, colours, luma):
    """Gadgeteer: brushed steel crowded with clusters of interlocking cogs (steel and brass: bevelled teeth, spokes,
    lathe-turned faces, hubs with screws), hex bolts and rivets in the gaps."""
    dark, ground, steel, brass = (_rgb(c) for c in colours)
    cv = _Canvas(h, w)
    brush = _brushed(rng, h, w)
    cv.rgb = _mix(np.broadcast_to(ground, (h, w, 3)), steel, 0.12 + 0.3 * brush) \
        * (0.9 + 0.15 * _fbm(rng, h, w, 3, 3))[..., None]
    module = 0.0045
    cogs = _cog_layout(rng, 600, 14, 0.008, 0.03, module, cv.W, cv.H)
    for x, y, r, ang, teeth, ex in sorted(cogs, key=lambda c: -c[2]):
        if ex < 0.38:
            colour = brass * (0.92 + 0.16 * ex)
        else:
            colour = _mix(steel, ground, np.float32((ex - 0.38) * 0.5)) * 1.08
        _cog(cv, x, y, r, ang, teeth, module, colour, ex)
    taken = (np.array([c[0] for c in cogs]), np.array([c[1] for c in cogs]), np.array([c[2] for c in cogs]))
    bx, by, br, bang, bex = _scatter(rng, 900, 0.0035, 0.006, gap=1.3, avoid=taken)
    for x, y, r, ang, ex in zip(bx, by, br, bang, bex):
        _hex_bolt(cv, x, y, r, ang, brass if ex < 0.25 else steel * 1.05)
    taken = (np.concatenate([taken[0], bx]), np.concatenate([taken[1], by]), np.concatenate([taken[2], br]))
    rx, ry, rr, _rang, rex = _scatter(rng, 2000, 0.0016, 0.0024, gap=1.6, avoid=taken)
    for x, y, r, ex in zip(rx, ry, rr, rex):
        _rivet(cv, x, y, r, steel * 0.95 if ex > 0.3 else brass)
    return cv.rgb


def _shape_flashes(rng, h, w, colours, luma):
    """Flashbang: white-hot bursts over a dark suit, each a blown-out core, a cool halo ring and thin lens spikes
    (four long, a few short), with faint after-image blooms between them."""
    dark, mid, cool, white = (_rgb(c) for c in colours)
    cv = _Canvas(h, w)
    haze = _fbm(rng, h, w, 3, 4)
    cv.rgb = _mix(np.broadcast_to(dark, (h, w, 3)), mid, haze ** 2 * 0.35)
    # The after-images: soft, dim blooms of the cool colour.
    ax, ay, ar, _aang, _aex = _scatter(rng, 300, 0.02, 0.045, gap=1.2)
    for x, y, r in zip(ax, ay, ar):
        iy, ix, X, Y = cv.tile(x, y, r * 2.5)
        cv.add(iy, ix, 0.16 * np.exp(-(X * X + Y * Y) / (r * r)), cool)
    xs, ys, rs, angs, extras = _scatter(rng, 400, 0.006, 0.016, gap=2.2, avoid=(ax, ay, ar * 0.2))
    for x, y, r, ang, ex in zip(xs, ys, rs, angs, extras):
        reach = r * 6.0
        iy, ix, X, Y = cv.tile(x, y, reach)
        d = np.hypot(X, Y)
        theta = np.arctan2(Y, X) - ang
        long_rays = np.exp(-np.abs(np.sin(2.0 * theta)) * 50.0) * np.exp(-d / (r * 3.2))
        short_rays = np.exp(-np.abs(np.sin(3.0 * theta + 0.5)) * 40.0) * np.exp(-d / (r * 1.0))
        ring = np.exp(-((d - r * (1.7 + 0.5 * ex)) / (r * 0.12)) ** 2) * 0.35
        cv.add(iy, ix, ring, cool)
        cv.add(iy, ix, 1.2 * long_rays + 0.5 * short_rays, white * 0.8 + cool * 0.2)
        cv.add(iy, ix, np.maximum(_cov(d - r * 0.45, cv.px), 0.9 * np.exp(-d / (r * 0.55))), white)
    return cv.rgb


def _shape_splatter(rng, h, w, colours, luma):
    """Slaughterhouse: a grimy dark apron grey with blood on it: ragged splashes (darker where they pooled, a brighter
    dried rim), their flung droplets, some drawn out into streaks, and a fine mist of specks."""
    dark, grime, blood, fresh = (_rgb(c) for c in colours)
    cv = _Canvas(h, w)
    cv.rgb = _ramp(_fbm(rng, h, w, 4, 5) ** 1.3, [colours[0], colours[1]])
    spots = _scatter(rng, 260, 0.012, 0.045, gap=1.5)
    for x, y, r, ang, ex in zip(*spots):
        iy, ix, X, Y = cv.tile(x, y, r * 3.0)
        d = np.hypot(X, Y)
        th = np.arctan2(Y, X)
        edge = 1.0
        for k in range(3, 9):
            edge = edge + rng.uniform(0.04, 0.16) / (k * 0.4) * np.sin(k * th + rng.uniform(0, _TAU))
        sd = d - r * edge
        cov = _cov(sd, cv.px)
        pooled = np.clip(-sd / (r * 0.6), 0.0, 1.0)
        col = _mix(np.broadcast_to(fresh, X.shape + (3,)), blood * 0.75, pooled * (0.6 + 0.4 * ex))
        cv.over(iy, ix, cov * (0.82 + 0.18 * ex), col)
        # Flung droplets around it, the far ones drawn out along their flight.
        for _ in range(int(6 + 10 * rng.random())):
            a = ang + rng.normal(0, 1.2)
            dist = r * rng.uniform(1.15, 2.7)
            dr = r * rng.uniform(0.05, 0.17)
            stretch = 1.0 + 2.5 * max(0.0, (dist / r) - 1.6)
            cx, cy = x + dist * np.cos(a), y + dist * np.sin(a)
            jy, jx, U, V = cv.tile(cx, cy, dr * stretch * 1.5 + cv.px * 2)
            u, v = _rot(U, V, a)
            dd = np.hypot(u / stretch, v) - dr
            cv.over(jy, jx, _cov(dd, cv.px) * 0.9, fresh * rng.uniform(0.7, 1.0))
    mx, my, mr, _ma, me = _scatter(rng, 2500, 0.0012, 0.0035, gap=1.2)
    for x, y, r, e in zip(mx, my, mr, me):
        iy, ix, X, Y = cv.tile(x, y, r + cv.px * 2)
        cv.over(iy, ix, _cov(np.hypot(X, Y) - r, cv.px) * (0.5 + 0.4 * e), fresh * 0.85)
    return cv.rgb


_SHAPES = {
    "blotch": _shape_blotch,
    "blotch-soft": lambda rng, h, w, c, l: _shape_blotch(rng, h, w, c, l, soft=True),
    "pixel": _shape_pixel,
    "stripe": _shape_stripe,
    "gold": _shape_gold,
    "flames": _shape_flames,
    "livery": _shape_livery,
    "cryo": _shape_cryo,
    "scanlines": _shape_scanlines,
    "noise": _shape_noise,
    "arcs": _shape_arcs,
    "cracks": _shape_cracks,
    "shrapnel": _shape_shrapnel,
    "hazard": _shape_hazard,
    "smoke": _shape_smoke,
    "chaff": _shape_chaff,
    "turnabout": _shape_turnabout,
    "minefield": _shape_minefield,
    "voltage": _shape_voltage,
    "cogs": _shape_cogs,
    "mastercraft": _shape_mastercraft,
    "flashes": _shape_flashes,
    "splatter": _shape_splatter,
}


def pattern_image(pattern: str, h: int, w: int, luma: np.ndarray, seed: str = "", mesh=None) -> np.ndarray:
    """The pattern's colours (h x w x 3, 0..1) for a texture of that size; seed (the texture's name) picks the layout.
    The gold finishes follow the retail brightness. The skeleton needs the body's mesh (skeleton.Mesh): it is drawn
    where the bones lie on the texture seed, or flat from the front for the swatch (seed "thumb")."""
    shape, colours = PATTERNS[pattern]
    if shape == "skeleton":
        from . import skeleton  # noqa: PLC0415

        if mesh is None:
            raise ValueError("the skeleton camo needs the body's mesh")
        layout = None if seed == "thumb" else skeleton.bake(mesh, seed, h, w)
        if layout is None and seed != "thumb":
            raise ValueError(f"{seed} is not a texture of the skeleton's mesh")
        return np.clip(skeleton.draw(layout, colours, h, w, mesh), 0.0, 1.0).astype(np.float32)
    if shape not in _SHAPES:
        raise ValueError(f"unknown camo shape {shape}")
    rgb = np.array(_SHAPES[shape](_rng(pattern, seed), h, w, colours, luma), dtype=np.float32)
    if shape in _WEATHERED:
        _fine_detail(_rng(pattern, seed + ":detail"), rgb)
    return np.clip(rgb, 0.0, 1.0)


def paint(retail: np.ndarray, pattern: str, seed: str = "", mesh=None) -> np.ndarray:
    """The camo over a retail RGBA texture (uint8, already at the output size): the pattern's colours times the retail
    shading, alpha kept."""
    rgb = retail[..., :3].astype(np.float32) / 255.0
    luma = rgb @ np.array([0.3, 0.59, 0.11], np.float32)
    h, w = luma.shape
    colours = pattern_image(pattern, h, w, luma, seed, mesh)
    shape = PATTERNS[pattern][0]
    if shape in _GILDED:
        camo = colours
    elif shape in _LACQUERED:
        # A light touch of the retail shading, so edges and details still read through the lacquer.
        shading = np.clip(0.5 + 0.5 * luma / max(float(luma.mean()), 1e-3), 0.8, 1.15)[..., None]
        camo = np.clip(colours * shading, 0.0, 1.0)
    else:
        shading = np.clip(luma / max(float(luma.mean()), 1e-3), 0.35, 1.8)[..., None]
        camo = np.clip(colours * shading, 0.0, 1.0)
    strength = FINISH_STRENGTH if shape in _FINISHES else CAMO_STRENGTH
    out = rgb * (1 - strength) + camo * strength
    result = retail.copy()
    result[..., :3] = np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8)
    return result


def output_size(package: str, pattern: str, w: int, h: int) -> tuple[int, int]:
    """A camo's size (w, h): the retail size times the package's factor, at least MIN_SIZE on the long side, map camos
    on bodies at most MAP_MAX; the aspect ratio kept."""
    long = max(max(w, h) * SCALE.get(package, 4), MIN_SIZE)
    if package == "SPerso" and pattern.startswith("map_"):
        long = min(long, max(MAP_MAX, max(w, h)))
    s = long / max(w, h)
    return max(1, round(w * s)), max(1, round(h * s))


def _paint_job(job: tuple) -> str:
    from PIL import Image  # noqa: PLC0415

    src, dst, pattern, stem, size, mesh_path = job
    retail = Image.open(src).convert("RGBA")
    if retail.size != size:
        retail = retail.resize(size, Image.LANCZOS)
    mesh = None
    if mesh_path:
        from .skeleton import Mesh  # noqa: PLC0415

        mesh = Mesh(Path(mesh_path))
    Image.fromarray(paint(np.asarray(retail, dtype=np.uint8), pattern, stem, mesh)).save(dst, compress_level=6)
    return dst


def _thumb_job(job: tuple) -> str:
    """The flat pattern at 1024, area-scaled to the swatch size (the gold finishes on a mid grey)."""
    from PIL import Image  # noqa: PLC0415

    pattern, dst, mesh_path = job
    n = 1024
    mesh = None
    if mesh_path:
        from .skeleton import Mesh  # noqa: PLC0415

        mesh = Mesh(Path(mesh_path))
    rgb = pattern_image(pattern, n, n, np.full((n, n), 0.5, np.float32), "thumb", mesh)
    k = n // THUMB_SIZE
    small = rgb.reshape(THUMB_SIZE, k, THUMB_SIZE, k, 3).mean((1, 3))
    Image.fromarray(np.clip(small * 255.0 + 0.5, 0, 255).astype(np.uint8)).save(dst, compress_level=6)
    return dst


def build_all(chars_dir: Path) -> dict:
    """Writes every target's camos next to its retail texture (a texture the install lacks is skipped) and a swatch per
    pattern under chars/thumbs. The work is spread over worker processes."""
    import concurrent.futures as cf  # noqa: PLC0415
    import multiprocessing as mp  # noqa: PLC0415

    from PIL import Image  # noqa: PLC0415

    jobs, skipped = [], []
    for package, stem, patterns in TARGETS:
        tex_dir = chars_dir / package / "textures"
        src = tex_dir / f"{stem}.png"
        if not src.is_file():
            skipped.append(f"{package}/{stem}")
            continue
        with Image.open(src) as im:
            w, h = im.size
        for pattern in patterns:
            mesh_path = None
            if PATTERNS[pattern][0] == "skeleton":
                mesh_path = chars_dir / package / SKELETON_MESH.get(stem, "")
                if stem not in SKELETON_MESH or not mesh_path.is_file():
                    skipped.append(f"{package}/{stem} ({pattern})")
                    continue
            jobs.append((str(src), str(tex_dir / f"cosmetic_{pattern}_{stem}.png"), pattern, stem,
                         output_size(package, pattern, w, h), str(mesh_path) if mesh_path else None))
    jobs.sort(key=lambda j: -j[4][0] * j[4][1])  # the big ones first, so the pool ends evenly
    thumbs_dir = chars_dir / "thumbs"
    thumbs_dir.mkdir(parents=True, exist_ok=True)
    spy_mesh = chars_dir / "SPerso" / "ATT_01.glb"
    thumbs = [(p, str(thumbs_dir / f"cosmetic_{p}.png"), str(spy_mesh) if PATTERNS[p][0] == "skeleton" else None)
              for p in PATTERNS if PATTERNS[p][0] != "skeleton" or spy_mesh.is_file()]
    workers = max(1, min(os.cpu_count() or 1, 12))
    with cf.ProcessPoolExecutor(max_workers=workers, mp_context=mp.get_context("spawn")) as pool:
        done = list(pool.map(_paint_job, jobs, chunksize=1))
        done_thumbs = list(pool.map(_thumb_job, thumbs, chunksize=1))
    return {"camos": len(done), "thumbs": len(done_thumbs), "skipped": skipped}
