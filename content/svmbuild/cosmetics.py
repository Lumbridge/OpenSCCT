"""OpenSCCT's own cosmetic camos (not retail): each pattern is painted over the player's own retail character and gun
textures, keeping their shading, as shared/chars/<Package>/textures/cosmetic_<pattern>_<texture>.png. The game's
SkeletalModel.SetCosmetic draws them (src/OpenScct.Net/Cosmetics/CosmeticCatalog.cs lists the items).

The pattern table below must match CosmeticCatalog.Patterns and CosmeticCatalog.Maps (ids and colours); the shapes are
drawn here procedurally with fixed seeds, so every install builds the same camos.
"""

from __future__ import annotations

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
    "golden": ("gold", [0x7A5A12, 0xD8A830, 0xFFE88A]),
    "laser_grid": ("scanlines", [0x140808, 0xE82020]),
    "night_ops": ("blotch", [0x0C0C0E, 0x1C1C20, 0x2C2C30]),
    "static": ("noise", [0x101418, 0x5A6A7A, 0xD8E4F0]),
    "shockwave": ("arcs", [0x08101E, 0x2A8AFF, 0xC8F0FF]),
    "breaker": ("cracks", [0x2A2A2A, 0x6E6E6A, 0xA8A8A0]),
    "frag": ("shrapnel", [0x0E0C0A, 0x3A2A1E, 0xD86A1E]),
    "lockdown": ("hazard", [0x101010, 0xE8C018]),
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
    "Slau": [0x1E0A0A, 0x5A1E1E, 0xA89A8A], "Stat": [0x1A1A14, 0x5A5236, 0xC2B47A],
    "Stee": [0x14161A, 0x464E5A, 0xA8AEB8], "Stro": [0x1A1810, 0x4E4630, 0x9A8E6A],
    "Term": [0x101A1E, 0x2E4E56, 0x8AB8B8], "Vert": [0x0E0E1A, 0x3A2E6E, 0xE89A3A],
    "Ware": [0x1E1A14, 0x5A4E3A, 0xA8987A], "Ziop": [0x140E1E, 0x46226A, 0x3AE8D8],
}
for _code, _palette in MAPS.items():
    PATTERNS["map_" + _code.lower()] = ("blotch", _palette)

_LADDER = ["woodland", "desert", "digital", "red_tiger", "blue_tiger"]
_MAP_PATTERNS = ["map_" + c.lower() for c in MAPS]

# Which retail textures take which patterns: (chars package folder, texture file stem, patterns).
TARGETS: list[tuple[str, str, list[str]]] = [
    ("SPerso", "SPersoTextures_Shadw_agent", _LADDER + ["night_ops", "static", "breaker"] + _MAP_PATTERNS),
    ("SPerso", "SPersoTextures_Shadw_agent_2", _LADDER + ["night_ops", "static", "breaker"] + _MAP_PATTERNS),
    ("SPerso", "SPersoTextures_DEF_01_A_Body", _LADDER + ["laser_grid", "frag", "lockdown"] + _MAP_PATTERNS),
    ("SWeaponFirst", "SWeaponTex_p190", _LADDER + ["static", "shockwave", "golden"]),
]
for _gun in ("SWeaponTex_1DEF_01", "SWeaponTex_M160", "SWeaponTex_Hk81", "SWeaponTex_HK_81_chargeur", "SWeaponTex_shotgun"):
    TARGETS.append(("SWeaponFirst", _gun, _LADDER + ["golden", "laser_grid", "frag"]))

# How much of the camo replaces the retail colour (the rest keeps small markings readable).
CAMO_STRENGTH = 0.85


def _rgb(c: int) -> np.ndarray:
    return np.array([(c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF], dtype=np.float32) / 255.0


def _rng(pattern: str, size: tuple[int, int]) -> np.random.Generator:
    return np.random.default_rng(zlib.crc32(f"{pattern}:{size[0]}x{size[1]}".encode()))


def _smooth_noise(rng: np.random.Generator, h: int, w: int, cells: int) -> np.ndarray:
    """Value noise: a random grid of cells x cells, bilinearly enlarged (tiles across the texture edges)."""
    from PIL import Image  # noqa: PLC0415

    grid = rng.random((cells, cells)).astype(np.float32)
    grid = np.pad(grid, ((0, 1), (0, 1)), mode="wrap")
    img = Image.fromarray(grid, mode="F").resize((w + w // cells, h + h // cells), Image.BICUBIC)
    return np.asarray(img, dtype=np.float32)[:h, :w]


def _fbm(rng: np.random.Generator, h: int, w: int, base: int, octaves: int = 3) -> np.ndarray:
    out = np.zeros((h, w), np.float32)
    amp, total, cells = 1.0, 0.0, base
    for _ in range(octaves):
        out += amp * _smooth_noise(rng, h, w, cells)
        total += amp
        amp *= 0.5
        cells *= 2
    out /= total
    lo, hi = np.percentile(out, 1), np.percentile(out, 99)
    return np.clip((out - lo) / max(hi - lo, 1e-6), 0.0, 1.0)


def _bands(value: np.ndarray, colours: list[int]) -> np.ndarray:
    index = np.clip((value * len(colours)).astype(np.int32), 0, len(colours) - 1)
    palette = np.stack([_rgb(c) for c in colours])
    return palette[index]


def pattern_image(pattern: str, h: int, w: int, luma: np.ndarray) -> np.ndarray:
    """The pattern's colours (h x w x 3, 0..1) for a texture of that size; gold follows the retail brightness."""
    shape, colours = PATTERNS[pattern]
    rng = _rng(pattern, (w, h))
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    if shape in ("blotch", "blotch-soft"):
        return _bands(_fbm(rng, h, w, 4 if shape == "blotch" else 3, 3 if shape == "blotch" else 2), colours)
    if shape == "pixel":
        block = max(2, min(h, w) // 48)
        coarse = _fbm(rng, max(1, h // block), max(1, w // block), 4, 2)
        coarse = np.repeat(np.repeat(coarse, block, axis=0), block, axis=1)[:h, :w]
        coarse = np.pad(coarse, ((0, h - coarse.shape[0]), (0, w - coarse.shape[1])), mode="edge")
        return _bands(coarse, colours)
    if shape == "stripe":
        warp = _fbm(rng, h, w, 3, 3)
        wave = np.sin((xx / w) * 2 * np.pi * 7 + warp * 9.0 + (yy / h) * 2.0)
        width = 0.35 + 0.4 * _fbm(rng, h, w, 4, 2)
        dark = wave > (1.0 - width)
        return np.where(dark[..., None], _rgb(colours[0]), _rgb(colours[1]))
    if shape == "gold":
        palette = np.stack([_rgb(c) for c in colours])
        t = np.clip(luma * 1.4, 0.0, 1.0) * (len(colours) - 1)
        i = np.clip(t.astype(np.int32), 0, len(colours) - 2)
        f = (t - i)[..., None]
        return palette[i] * (1 - f) + palette[i + 1] * f
    if shape == "scanlines":
        period = max(4, h // 32)
        lines = (yy.astype(np.int32) % period) < max(1, period // 4)
        grid = (xx.astype(np.int32) % (period * 4)) < 1
        glow = np.clip(lines * 1.0 + grid * 0.5, 0, 1)[..., None]
        return _rgb(colours[0]) * (1 - glow) + _rgb(colours[1]) * glow
    if shape == "noise":
        fine = rng.random((h, w)).astype(np.float32)
        bands = (np.floor(yy / max(2, h // 64)) % 7 == 0)[..., None]
        base = _bands(0.6 * fine + 0.4 * _fbm(rng, h, w, 8, 2), colours)
        return np.where(bands, _rgb(colours[-1]), base)
    if shape == "arcs":
        n = _fbm(rng, h, w, 3, 4)
        vein = np.clip(1.0 - np.abs(n - 0.5) * 40.0, 0.0, 1.0)
        halo = np.clip(1.0 - np.abs(n - 0.5) * 10.0, 0.0, 1.0)
        out = _rgb(colours[0]) * np.ones((h, w, 1), np.float32)
        out = out * (1 - halo[..., None]) + _rgb(colours[1]) * halo[..., None]
        return out * (1 - vein[..., None]) + _rgb(colours[2]) * vein[..., None]
    if shape == "cracks":
        a, b = _fbm(rng, h, w, 5, 3), _fbm(rng, h, w, 5, 3)
        crack = np.clip(1.0 - np.abs(a - b) * 30.0, 0.0, 1.0)[..., None]
        base = _bands(0.5 + 0.5 * (_fbm(rng, h, w, 3, 2) - 0.5), colours[1:])
        return base * (1 - crack) + _rgb(colours[0]) * crack
    if shape == "shrapnel":
        base = _bands(_fbm(rng, h, w, 4, 3) * 0.99, colours[:2])
        sparks = (rng.random((h, w)) > 0.985) | (_fbm(rng, h, w, 12, 1) > 0.9)
        return np.where(sparks[..., None], _rgb(colours[2]), base)
    if shape == "hazard":
        period = max(8, min(h, w) // 8)
        stripe = ((xx + yy).astype(np.int32) // period) % 2 == 0
        return np.where(stripe[..., None], _rgb(colours[1]), _rgb(colours[0]))
    raise ValueError(f"unknown camo shape {shape}")


def paint(retail: np.ndarray, pattern: str) -> np.ndarray:
    """The camo over a retail RGBA texture (uint8): the pattern's colours times the retail shading, alpha kept."""
    rgb = retail[..., :3].astype(np.float32) / 255.0
    luma = rgb @ np.array([0.3, 0.59, 0.11], np.float32)
    h, w = luma.shape
    colours = pattern_image(pattern, h, w, luma)
    if PATTERNS[pattern][0] == "gold":
        camo = colours
    else:
        shading = np.clip(luma / max(float(luma.mean()), 1e-3), 0.35, 1.8)[..., None]
        camo = np.clip(colours * shading, 0.0, 1.0)
    out = rgb * (1 - CAMO_STRENGTH) + camo * CAMO_STRENGTH
    result = retail.copy()
    result[..., :3] = np.clip(out * 255.0 + 0.5, 0, 255).astype(np.uint8)
    return result


def build_all(chars_dir: Path) -> dict:
    """Writes every target's camos next to its retail texture; a texture the install lacks is skipped."""
    from PIL import Image  # noqa: PLC0415

    written, skipped = 0, []
    for package, stem, patterns in TARGETS:
        tex_dir = chars_dir / package / "textures"
        src = tex_dir / f"{stem}.png"
        if not src.is_file():
            skipped.append(f"{package}/{stem}")
            continue
        retail = np.asarray(Image.open(src).convert("RGBA"), dtype=np.uint8)
        for pattern in patterns:
            Image.fromarray(paint(retail, pattern), mode="RGBA").save(tex_dir / f"cosmetic_{pattern}_{stem}.png", optimize=True)
            written += 1
    return {"camos": written, "skipped": skipped}
