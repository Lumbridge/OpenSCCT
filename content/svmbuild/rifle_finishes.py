"""Fit the existing assault-rifle finishes to the painted panels of the four licensed rifles."""
from pathlib import Path
from tempfile import NamedTemporaryFile

import numpy as np
from PIL import Image

from . import cosmetic_art
from .cosmetics import TARGETS
from .fitted import SurfaceMesh
from .gunskin import _sample
from .skeleton import bake


# World GLBs are in metres: +X towards the muzzle, +Y up. Limits select the casing and leave the
# grip, recoil pad, trigger, sight line and barrel hardware in their original materials. The M4
# and AK pack moving parts into the same atlas as their casing, so select their casing primitive.
# Additional GLBs in OpenScctRifles do not inherit these material or finish assignments.
PANELS = {
    'm4a1s': {'m4a1s_6': (4, -.028, .435, .008, .121)},
    'ak47': {'ak47_0': (0, -.065, .330, .020, .115)},
    'galil': {'galil_0': (0, .008, .247, .059, .119),
              'galil_12': (0, -.010, .389, .040, .122)},
    'famas': {'famas_0': (0, -.350, .237, .022, .151),
              'famas_2': (0, -.125, .232, .101, .185)},
}
FINISHES = tuple(next(patterns for _, stem, patterns in TARGETS if stem == 'SWeaponTex_M160'))


def projection(mesh, stem, bounds, width, height):
    primitive, left, right, bottom, top = bounds
    groups = np.repeat(np.arange(len(mesh.tris[stem])), [len(tris) for tris in mesh.tris[stem]])
    # Protect a functional part wherever it shares UVs with a paintable primitive.
    layout = bake(mesh, stem, height, width, priority=(groups != primitive).astype(np.float32))
    x, y, z = layout['P'].transpose(2, 0, 1)
    nx, ny, nz = np.abs(layout['N']).transpose(2, 0, 1)
    mask = (layout['cover'] & (groups[layout['tri']] == primitive) & (x >= left) & (x <= right)
            & (y >= bottom) & (y <= top))
    # Project the source artwork in the same physical frame across all UV islands and material
    # slots. Top and end faces get their own plane so the pattern never collapses to a line.
    u = np.where((nx > ny) & (nx > nz), z, x) / .28
    v = np.where((ny > nx) & (ny > nz), z, -y) / .28
    return mask, u[mask], v[mask]


def panel_colour(base, mask, u, v, pattern):
    """Keep full-resolution source details, original alpha and the uncoated pixels intact."""
    size = (base.shape[1], base.shape[0])
    projected = np.zeros((*mask.shape, 4), np.uint8)
    projected[mask, :3] = np.rint(_sample(cosmetic_art.load(pattern), u, v) * 255).astype(np.uint8)
    projected[mask, 3] = 255
    # Pillow resamples RGBA in premultiplied space, avoiding dark fringes at panel boundaries.
    fitted = np.asarray(Image.fromarray(projected).resize(size, Image.Resampling.BILINEAR))
    rgb = base[..., :3].astype(np.float32)
    r, g, b = rgb.transpose(2, 0, 1)
    # Brown wood and brass as well as bright stamps and maker's marks share these diffuse maps.
    warm = (r > g * 1.13 + 2) & (g > b * 1.07 + 1)
    markings = rgb.max(2) > 155
    luma = .299 * r + .587 * g + .114 * b
    allowed = (fitted[..., 3] > 0) & ~warm & ~markings & (luma > 2)
    if not allowed.any():
        raise ValueError('No paintable texels on rifle panel')
    median = max(float(np.median(luma[allowed])), 1.)
    shading = np.clip(.34 + .66 * luma[allowed] / median, .34, 1.45)
    strength = fitted[..., 3][allowed].astype(np.float32) * (.83 / 255)
    coating = fitted[..., :3][allowed] * shading[:, None]
    painted = base.copy()
    painted[allowed, :3] = np.clip(np.rint(rgb[allowed] + (coating - rgb[allowed]) * strength[:, None]), 0, 255)
    sheen = np.where(allowed, fitted[..., 3], 0).astype(np.uint8)
    return painted, sheen


def _atomic_png(pixels, output):
    with NamedTemporaryFile(dir=output.parent, suffix='.tmp', delete=False) as temp:
        temporary = Path(temp.name)
    try:
        Image.fromarray(pixels).save(temporary, format='PNG', compress_level=4)
        temporary.replace(output)
    finally:
        temporary.unlink(missing_ok=True)


def install(package, source, patterns=FINISHES):
    """Install only generated colour variants. Both first and third person use the same atlas."""
    textures = package / 'textures'
    textures.mkdir(parents=True, exist_ok=True)
    count = 0
    for weapon, panels in PANELS.items():
        mesh = SurfaceMesh(source / (weapon + '.glb'))
        for stem, bounds in panels.items():
            with Image.open(source / 'textures' / (stem + '.png')) as native:
                base = np.asarray(native.convert('RGBA'))
            h, w = base.shape[:2]
            scale = min(1., 1024 / max(w, h))
            fitting = projection(mesh, stem, bounds, round(w * scale), round(h * scale))
            for pattern in patterns:
                painted, sheen = panel_colour(base, *fitting, pattern)
                image = textures / f'cosmetic_{pattern}_{stem}.png'
                _atomic_png(painted, image)
                _atomic_png(sheen, image.with_suffix('.mask.png'))
                count += 1
    return count
