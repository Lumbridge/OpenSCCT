"""Authored cosmetic artwork, sampled onto fitted meshes without changing its pixels.

The PNGs are original imagegen outputs. Missing artwork is an error: preparation
must never silently replace an earned finish with an old procedural substitute.
"""
from __future__ import annotations

import json
from functools import lru_cache
from pathlib import Path

import numpy as np

SOURCE = Path(__file__).resolve().parents[1] / 'assets' / 'cosmetics'


def source_files():
    from .cosmetics import PATTERNS
    files = [SOURCE / 'textures' / (pattern + '.png') for pattern in sorted(PATTERNS)]
    files.append(SOURCE / 'textures' / 'skeleton_back.png')
    files.append(SOURCE / 'skeleton-layout.json')
    missing = [str(path) for path in files if not path.is_file()]
    if missing:
        raise FileNotFoundError('Missing authored cosmetic artwork: ' + ', '.join(missing))
    return files


@lru_cache(maxsize=6)
def _load(path: str, stamp: int, size: int) -> np.ndarray:
    from PIL import Image
    with Image.open(path) as image:
        if image.width < 256 or image.height < 256:
            raise ValueError(f'Cosmetic artwork is too small: {path}')
        if image.mode in ('RGBA', 'LA') and image.getextrema()[-1] != (255, 255):
            raise ValueError(f'Cosmetic artwork must be opaque: {path}')
        pixels = np.asarray(image.convert('RGB'), dtype=np.float32) / 255.0
    pixels.setflags(write=False)
    return pixels


def load(pattern: str) -> np.ndarray:
    from .cosmetics import PATTERNS
    if pattern not in PATTERNS and pattern != 'skeleton_back':
        raise ValueError(f'Unknown cosmetic pattern: {pattern}')
    path = SOURCE / 'textures' / (pattern + '.png')
    try:
        stat = path.stat()
    except FileNotFoundError as error:
        raise FileNotFoundError(f'Missing authored cosmetic artwork: {path}') from error
    return _load(str(path), stat.st_mtime_ns, stat.st_size)


def swatch(pattern: str, h: int, w: int) -> np.ndarray:
    from PIL import Image
    pixels = load(pattern)
    image = Image.fromarray(np.round(pixels * 255).astype(np.uint8))
    return np.asarray(image.resize((w, h), Image.Resampling.LANCZOS), dtype=np.float32) / 255.0


def _clamped_sample(image, u, v):
    """Bilinear sampling with edge clamping, for non-repeating anatomy regions."""
    from .gunskin import _sample
    height, width = image.shape[:2]
    return _sample(image, np.clip(u, .5 / width, 1 - .5 / width),
                   np.clip(v, .5 / height, 1 - .5 / height))


def _arm_coordinates(mesh, group, points, rectangle, image_shape):
    """Follow the shoulder/elbow/wrist line without inflating bones to padded sleeves."""
    from . import skeleton
    side = 'L' if group == skeleton.ARM_L else 'R'
    joints = [mesh.joints[f'B {side} {part}'] for part in ('UpperArm', 'ForeArm', 'Hand')]
    ordered = sorted(joints, key=lambda joint: float(joint[0]))
    xs = np.array([joint[0] for joint in ordered])
    ys = np.array([joint[1] for joint in ordered])
    length = max(float(xs[-1] - xs[0]), 1e-5)
    center = np.interp(points[:, 0], xs, ys)
    x0, y0, x1, y1 = rectangle
    aspect = (x1 - x0) * image_shape[1] / ((y1 - y0) * image_shape[0])
    return np.stack([(points[:, 0] - xs[0]) / length,
                     .5 + (points[:, 1] - center) * aspect / length], -1)


def skeleton_image(mesh, texture, h, w):
    """Fit each authored anatomical region to the matching bind-pose body part.

Bounds come from the entire model, not individual atlases, so split materials
cannot stretch a fragment into an entire limb. Hands/feet project from above;
other parts project from the front and fade gently around their side edges.
"""
    from . import skeleton
    image = load('skeleton')
    back_image = load('skeleton_back')
    metadata = json.loads((SOURCE / 'skeleton-layout.json').read_text(encoding='utf-8'))
    regions, back_regions = metadata['regions'], metadata['back_regions']
    layout = skeleton.bake(mesh, texture, h, w)
    if layout is None:
        raise ValueError(f'{texture} is not a texture of the skeleton mesh')
    group_names = ('head', 'torso', 'arm_left', 'arm_right', 'hand_left', 'hand_right',
                   'leg_left', 'leg_right', 'foot_left', 'foot_right')
    base = np.median(image[:max(1, image.shape[0] // 100)], axis=(0, 1))
    out = np.broadcast_to(base, (h, w, 3)).copy()
    vertex_groups = mesh.groups.argmax(1)
    for group, name in enumerate(group_names):
        region = layout['cover'] & (layout['group'] == group)
        vertices = mesh.pos[vertex_groups == group]
        if not region.any() or not len(vertices):
            continue
        x0, y0, x1, y1 = regions[name]
        if not (0 <= x0 < x1 <= 1 and 0 <= y0 < y1 <= 1):
            raise ValueError(f'Invalid authored skeleton region: {name}')
        p = layout['P'][region]
        normal = layout['N'][region]
        # Hands and feet expose their dorsal surfaces along Y; all other bones
        # use the character's frontal XY plane. A hand points along X in T-pose.
        top = group in (skeleton.HAND_L, skeleton.HAND_R, skeleton.FOOT_L, skeleton.FOOT_R)
        axes = (0, 2) if top else (0, 1)
        lo = vertices[:, axes].min(0)
        span = np.maximum(np.ptp(vertices[:, axes], axis=0), 1e-5)
        q = np.clip((p[:, axes] - lo) / span, 0, 1)
        arm = group in (skeleton.ARM_L, skeleton.ARM_R)
        if arm:
            q = _arm_coordinates(mesh, group, p, regions[name], image.shape)
        u = x0 + q[:, 0] * (x1 - x0)
        v = y1 - q[:, 1] * (y1 - y0)
        if group in (skeleton.FOOT_L, skeleton.FOOT_R):
            # Positive Z points towards the toes: they are at the bottom of the
            # source artwork, unlike a body's positive-Y head at the top.
            v = y0 + q[:, 1] * (y1 - y0)
        ink = _clamped_sample(image, u, v)
        inside = np.all((q >= 0) & (q <= 1), axis=1)
        ink = np.where(inside[:, None], ink, base)
        if not top:
            bx0, by0, bx1, by1 = back_regions[name]
            if not (0 <= bx0 < bx1 <= 1 and 0 <= by0 < by1 <= 1):
                raise ValueError(f'Invalid posterior skeleton region: {name}')
            # Seen from behind, model +X is image left. Both the source part
            # selection and its local horizontal coordinate must change sides.
            back_q = _arm_coordinates(mesh, group, p, back_regions[name], back_image.shape) if arm else q
            back_u = bx1 - back_q[:, 0] * (bx1 - bx0)
            back_v = by1 - back_q[:, 1] * (by1 - by0)
            rear = _clamped_sample(back_image, back_u, back_v)
            rear = np.where(np.all((back_q >= 0) & (back_q <= 1), axis=1)[:, None], rear, base)
            ink = np.where((normal[:, 2] < 0)[:, None], rear, ink)
        visibility = np.clip((np.abs(normal[:, 1 if top else 2]) - .1) / .5, 0, 1)
        out[region] = base + (ink - base) * visibility[:, None]
    return out
