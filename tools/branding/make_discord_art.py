"""Draws the Discord Rich Presence art assets into tools/branding/discord/ (docs/DISCORD.md).

    python tools/branding/make_discord_art.py

Upload each PNG under the app's Rich Presence -> Art Assets in the Discord Developer Portal with its file name as the
key: logo (the large image), spy and merc (the small corner badge, which Discord crops to a circle). All 1024 x 1024,
our own art in the look of make_branding.py: the mark for the logo, three night-vision lenses for the spy, a red
reticle for the mercenary.
"""

from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

from make_branding import BG, DIM, GREEN, TILE, draw_mark

ROOT = Path(__file__).resolve().parents[2]
OUT = Path(__file__).resolve().parent / "discord"

SIZE = 1024
SCALE = 4
RED = (255, 84, 64)
RED_DIM = (150, 40, 32)
MERC_TILE = (24, 14, 13)


def glow(layer: Image.Image, radius: float, strength: float) -> Image.Image:
    """A soft halo of the layer's own colour behind it."""
    halo = layer.filter(ImageFilter.GaussianBlur(radius))
    if strength != 1.0:
        alpha = halo.getchannel("A").point(lambda a: min(255, int(a * strength)))
        halo.putalpha(alpha)
    out = Image.new("RGBA", layer.size, (0, 0, 0, 0))
    out.alpha_composite(halo)
    out.alpha_composite(layer)
    return out


def scanlines(im: Image.Image, colour: tuple[int, int, int], step: int, alpha: int) -> None:
    """Faint horizontal lines across the background, like a night-vision screen."""
    d = ImageDraw.Draw(im)
    for y in range(0, im.height, step):
        d.line([(0, y), (im.width, y)], fill=colour + (alpha,), width=max(1, step // 4))


def vignette(size: int, inner: tuple[int, int, int], outer: tuple[int, int, int]) -> Image.Image:
    """A radial fade from the centre colour to the edge colour."""
    small = 256
    im = Image.new("RGB", (small, small))
    px = im.load()
    c = (small - 1) / 2
    for y in range(small):
        for x in range(small):
            t = min(1.0, math.hypot(x - c, y - c) / (small * 0.62))
            t = t * t
            px[x, y] = tuple(int(inner[i] + (outer[i] - inner[i]) * t) for i in range(3))
    return im.resize((size, size), Image.Resampling.BICUBIC).convert("RGBA")


def logo() -> Image.Image:
    """The mark, glowing, on the dark tile with scanlines (the large image)."""
    im = vignette(SIZE, (22, 34, 26), BG)
    scanlines(im, (0, 0, 0), 8, 30)
    mark = draw_mark(int(SIZE * 0.8), tile=False)
    layer = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    off = (SIZE - mark.width) // 2
    layer.alpha_composite(mark, (off, off))
    im.alpha_composite(glow(layer, SIZE * 0.025, 1.4))
    return im


def badge_base(fill: tuple[int, int, int], edge: tuple[int, int, int], ring: tuple[int, int, int]) -> tuple[Image.Image, ImageDraw.ImageDraw, int]:
    """A round badge drawn at SCALE x: dark disc, coloured ring. Returns the image, its pen and its size."""
    s = SIZE * SCALE
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    disc = vignette(s, fill, edge)
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).ellipse([0, 0, s - 1, s - 1], fill=255)
    im.paste(disc, (0, 0), mask)
    d = ImageDraw.Draw(im)
    w = int(s * 0.035)
    d.ellipse([w // 2, w // 2, s - 1 - w // 2, s - 1 - w // 2], outline=ring + (255,), width=w)
    return im, d, s


def spy() -> Image.Image:
    """Three night-vision lenses, one above two, glowing green in a dark disc."""
    im, _, s = badge_base(TILE, BG, DIM)
    lenses = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(lenses)
    c = s / 2
    r = s * 0.112
    spots = [(c, c - s * 0.15), (c - s * 0.2, c + s * 0.12), (c + s * 0.2, c + s * 0.12)]
    # The strap behind the lenses.
    d.rounded_rectangle([c - s * 0.36, c + s * 0.09, c + s * 0.36, c + s * 0.15], radius=int(s * 0.04), fill=DIM + (255,))
    for x, y in spots:
        d.ellipse([x - r * 1.18, y - r * 1.18, x + r * 1.18, y + r * 1.18], fill=(30, 46, 34, 255))
        d.ellipse([x - r, y - r, x + r, y + r], fill=GREEN + (255,))
        d.ellipse([x - r * 0.42 - r * 0.25, y - r * 0.42 - r * 0.25, x - r * 0.42 + r * 0.2, y - r * 0.42 + r * 0.2],
                  fill=(225, 255, 230, 255))
    im.alpha_composite(glow(lenses, s * 0.03, 1.3))
    return im.resize((SIZE, SIZE), Image.Resampling.LANCZOS)


def merc() -> Image.Image:
    """A red reticle in a dark disc: broken ring, crosshair ticks and a centre dot."""
    im, _, s = badge_base(MERC_TILE, (10, 6, 6), RED_DIM)
    reticle = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(reticle)
    c = s / 2
    r = s * 0.27
    w = int(s * 0.05)
    # The ring, broken where the crosshair lines cross it.
    for start in (20, 110, 200, 290):
        d.arc([c - r, c - r, c + r, c + r], start=start, end=start + 50, fill=RED + (255,), width=w)
    t_in, t_out, tw = s * 0.12, s * 0.38, s * 0.028
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        x0, y0 = c + dx * t_in, c + dy * t_in
        x1, y1 = c + dx * t_out, c + dy * t_out
        d.rectangle([min(x0, x1) - (tw if dx == 0 else 0), min(y0, y1) - (tw if dy == 0 else 0),
                     max(x0, x1) + (tw if dx == 0 else 0), max(y0, y1) + (tw if dy == 0 else 0)], fill=RED + (255,))
    d.ellipse([c - s * 0.045, c - s * 0.045, c + s * 0.045, c + s * 0.045], fill=(255, 190, 170, 255))
    im.alpha_composite(glow(reticle, s * 0.025, 1.3))
    return im.resize((SIZE, SIZE), Image.Resampling.LANCZOS)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for name, make in (("logo", logo), ("spy", spy), ("merc", merc)):
        make().save(OUT / f"{name}.png", optimize=True)
    print(f"wrote logo.png, spy.png, merc.png ({SIZE} px) to {OUT}")


if __name__ == "__main__":
    main()
