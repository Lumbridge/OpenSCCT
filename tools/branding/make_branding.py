"""Draws the OpenSCCT icon and boot splash into game/branding/.

    python tools/branding/make_branding.py

The mark is our own: a ring (the O of Open) with a horizontal scan gap and four aiming ticks, night-vision green on
near-black. Outputs: icon.svg (project icon), icon.png (256 px), icon.ico (16 to 256 px, for the Windows exe) and
splash.png (1280 x 720 boot splash). The wordmark uses Bahnschrift from Windows; elsewhere pass --font <ttf>.
"""

from __future__ import annotations

import argparse
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "game" / "branding"

BG = (10, 13, 11)
TILE = (16, 22, 18)
GREEN = (120, 255, 140)
DIM = (58, 140, 74)


def draw_mark(size: int, tile: bool) -> Image.Image:
    """The mark on a transparent square, drawn at 8x and scaled down for smooth edges."""
    s = size * 8
    im = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    if tile:
        d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.2), fill=TILE + (255,))
    c = s / 2
    cut = TILE + (255,) if tile else (0, 0, 0, 0)
    r_out, r_in = s * 0.34, s * 0.25
    d.ellipse([c - r_out, c - r_out, c + r_out, c + r_out], fill=GREEN + (255,))
    d.ellipse([c - r_in, c - r_in, c + r_in, c + r_in], fill=cut)
    gap = s * 0.035
    d.rectangle([c - r_out - 1, c - gap, c + r_out + 1, c + gap], fill=cut)
    d.ellipse([c - s * 0.045, c - s * 0.045, c + s * 0.045, c + s * 0.045], fill=GREEN + (255,))
    t_len, t_w = s * 0.07, s * 0.022
    for dx, dy in ((0, -1), (0, 1), (-1, 0), (1, 0)):
        x0 = c + dx * (r_out + s * 0.03)
        y0 = c + dy * (r_out + s * 0.03)
        x1 = x0 + dx * t_len
        y1 = y0 + dy * t_len
        d.rectangle([min(x0, x1) - (t_w if dx == 0 else 0), min(y0, y1) - (t_w if dy == 0 else 0),
                     max(x0, x1) + (t_w if dx == 0 else 0), max(y0, y1) + (t_w if dy == 0 else 0)], fill=DIM + (255,))
    return im.resize((size, size), Image.Resampling.LANCZOS)


def svg() -> str:
    """The same mark as SVG on a 256 tile (the project icon Godot shows in the editor and the window)."""
    s, c = 256, 128
    r_out, r_in = s * 0.34, s * 0.25
    gap = s * 0.035
    ticks = []
    t_len, t_w = s * 0.07, s * 0.022
    o = r_out + s * 0.03
    ticks.append(f'<rect x="{c - t_w:.2f}" y="{c - o - t_len:.2f}" width="{2 * t_w:.2f}" height="{t_len:.2f}"/>')
    ticks.append(f'<rect x="{c - t_w:.2f}" y="{c + o:.2f}" width="{2 * t_w:.2f}" height="{t_len:.2f}"/>')
    ticks.append(f'<rect x="{c - o - t_len:.2f}" y="{c - t_w:.2f}" width="{t_len:.2f}" height="{2 * t_w:.2f}"/>')
    ticks.append(f'<rect x="{c + o:.2f}" y="{c - t_w:.2f}" width="{t_len:.2f}" height="{2 * t_w:.2f}"/>')
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <defs>
    <mask id="cut">
      <rect width="256" height="256" fill="white"/>
      <circle cx="{c}" cy="{c}" r="{r_in:.2f}" fill="black"/>
      <rect x="0" y="{c - gap:.2f}" width="256" height="{2 * gap:.2f}" fill="black"/>
    </mask>
  </defs>
  <rect width="256" height="256" rx="{s * 0.2:.2f}" fill="rgb{TILE}"/>
  <circle cx="{c}" cy="{c}" r="{r_out:.2f}" fill="rgb{GREEN}" mask="url(#cut)"/>
  <circle cx="{c}" cy="{c}" r="{s * 0.045:.2f}" fill="rgb{GREEN}"/>
  <g fill="rgb{DIM}">{''.join(ticks)}</g>
</svg>
"""


def splash(font_path: Path) -> Image.Image:
    w, h = 1280, 720
    im = Image.new("RGBA", (w, h), BG + (255,))
    mark = draw_mark(220, tile=False)
    im.alpha_composite(mark, ((w - 220) // 2, 170))
    font = ImageFont.truetype(str(font_path), 84)
    d = ImageDraw.Draw(im)
    text = "OpenSCCT"
    box = d.textbbox((0, 0), text, font=font)
    tw = box[2] - box[0]
    x, y = (w - tw) // 2, 430
    open_w = d.textbbox((0, 0), "Open", font=font)[2]
    d.text((x, y), "Open", font=font, fill=(225, 235, 228, 255))
    d.text((x + open_w, y), "SCCT", font=font, fill=GREEN + (255,))
    d.rectangle([(w - 160) // 2, 548, (w + 160) // 2, 551], fill=DIM + (255,))
    return im


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--font", type=Path, default=Path("C:/Windows/Fonts/bahnschrift.ttf"))
    args = ap.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "icon.svg").write_text(svg(), encoding="utf-8", newline="\n")
    big = draw_mark(256, tile=True)
    big.save(OUT / "icon.png")
    big.save(OUT / "icon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    splash(args.font).convert("RGB").save(OUT / "splash.png")
    print(f"wrote icon.svg, icon.png, icon.ico, splash.png to {OUT}")


if __name__ == "__main__":
    main()
