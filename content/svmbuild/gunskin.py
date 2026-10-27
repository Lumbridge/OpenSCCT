"""Every gun and suit skin drawn on the mesh itself, not across its texture.

A gun or body texture is a UV atlas of many small islands (a side of the receiver here, the top of the barrel there),
so a pattern drawn across the texture lands on each piece as a random slice of it, at that island's own size and
turned its own way. Instead, as for the skeleton camo, each texel is traced back to the point of the mesh it covers
(skeleton.bake), and the skin is drawn there in metres.

Guns. Each gun has its frame (forward to the muzzle, up and side, per mesh: GUN_MESH) and its pieces: the mesh's
separate shells (welded by position), each sorted by its shape into the body (receiver, stock, grip), barrels (long,
thin and along the gun: barrels, handguards, scope tubes), hanging pieces (magazines, separate grips) and small parts
(sights, levers, bolts). Each texel's face (side, top or end, from its normal) picks the plane a flat motif is drawn in.

- Kaido (racing livery), Cryo (sci-fi panels) and Pyre (hot-rod flames) are designed piece by piece: the body takes
  the main design in the gun's frame, barrels take rings along their own axis, hanging pieces their own design along
  their length and small parts an accent.
- Every other skin is its flat pattern (cosmetics' shapes, drawn once as a wrapping tile) laid over the whole gun at
  one real size (TILE_M), so it runs on from piece to piece and every gun shows it at the same scale: the sides see it
  from the side, the top from above, barrels have it wrapped round them. The blotch camos are computed in 3D instead
  (solid noise the surface cuts through: no seams at all). Then trim: a dark muzzle with an accent ring on each barrel,
  a dark foot plate on magazines and grips, dark small parts.

The merc's sleeves (his first-person arms share the gun's skin) are painted as a suit: Kaido's and Cryo's as racing
suits, Pyre's with flames climbing from the cuffs, the rest with the pattern wrapped round each arm and dark cuffs.

Suits (draw_suit). The body's bind pose takes the same tiles, seen from the front, the sides and above, blended by
which way each texel faces; the blotch camos are again solid 3D noise, and tiger stripes are turned to wrap round the
torso and legs. Galaxy (a Doppler nebula with stars) and Inferno (flames rising from the boots and the hands) are solid
3D designs of their own. The body's atlas shares islands between left and right and front and back, so the body is
folded to one quarter (|x|, |z|) before anything is drawn on it.

Shapes are measured against the gun's size (its length and height), so every gun gets the same design at its own
scale. Edges are anti-aliased over about one texel, from how far the surface moves per texel.
"""

from __future__ import annotations

import functools

import numpy as np

from .cosmetics import _cov, _dome, _mix, _rgb, _sd_box, _sd_chevron, _sd_segment, _span, _stripe
from .skeleton import ARM_L, ARM_R, HAND_L, HAND_R, Mesh, bake

# Retail gun texture: (the mesh it is painted from, forward and up in that mesh's space, whether it is the merc's
# sleeves rather than the gun).
_X, _MX, _Y = (1.0, 0.0, 0.0), (-1.0, 0.0, 0.0), (0.0, 1.0, 0.0)
GUN_MESH: dict[str, tuple[str, tuple, tuple, bool]] = {
    "SWeaponTex_p190": ("p190.glb", _X, _Y, False),
    "SWeaponTex_Hk81": ("Hk81.glb", _X, _Y, False),
    "SWeaponTex_HK_81_chargeur": ("Hk81.glb", _X, _Y, False),
    "SWeaponTex_M160": ("M160.glb", _X, _Y, False),
    "SWeaponTex_shotgun": ("BM401.glb", _X, _Y, False),
    # The merc's first-person sleeves and gloves (his rig points the gun down -x): a racing suit, not a gun.
    "SWeaponTex_1DEF_01": ("1DEF_01.glb", _MX, _Y, True),
}

BODY, BARREL, HANG, SMALL, SLEEVE = range(5)


# ---------------------------------------------------------------------------------------------------------------------
# The gun: its frame, pieces and each texel's place on it

class _Gun:
    def __init__(self, mesh: Mesh, texture: str, h: int, w: int, forward, up, sleeves: bool = False):
        F = np.asarray(forward, np.float32)
        U = np.asarray(up, np.float32)
        S = np.cross(F, U)
        # Faces that share texels (a magazine's six faces each mapping the whole texture) leave them to the most
        # sideways one: the sides are what the player sees.
        own = np.concatenate(mesh.tris[texture])
        tp = mesh.pos[own]
        face_n = np.cross(tp[:, 1] - tp[:, 0], tp[:, 2] - tp[:, 0])
        side_ness = np.abs(face_n @ S) / np.maximum(np.linalg.norm(face_n, axis=1), 1e-12)
        layout = bake(mesh, texture, h, w, side_ness)
        if layout is None:
            raise ValueError(f"{texture} is not a texture of the gun's mesh")
        self.cover = layout["cover"]
        P, N = layout["P"], layout["N"]
        self.x, self.y, self.z = P @ F, P @ U, P @ S
        nf, nu, ns = np.abs(N @ F), np.abs(N @ U), np.abs(N @ S)
        self.side = (ns >= nf) & (ns >= nu)
        self.top = ~self.side & (nu >= nf)
        self.end = ~self.side & ~self.top

        # How far the surface moves per texel along each texture axis (the smaller of the steps to either neighbour,
        # so a jump to another island does not count), and from that the metres per texel.
        self.Ji, self.Jj = (self._step(P, ax) for ax in (0, 1))
        mpp = np.maximum(np.linalg.norm(self.Ji, axis=2), np.linalg.norm(self.Jj, axis=2))
        med = float(np.median(mpp[self.cover])) if self.cover.any() else 1e-3
        self.mpp = np.clip(mpp, 0.25 * med, 4.0 * med).astype(np.float32)

        # The gun's extent over the whole mesh (every texture), so a magazine on its own texture still sits in the
        # same livery as the rest.
        allv = mesh.pos
        gx, gy, gz = allv @ F, allv @ U, allv @ S
        self.x0, self.x1 = float(gx.min()), float(gx.max())
        self.y0, self.y1 = float(gy.min()), float(gy.max())
        self.length = self.x1 - self.x0
        self.height = self.y1 - self.y0
        # The size motifs and lines are measured against: a long, slim gun (the shotgun) takes the same sizes as a
        # short, deep one, and the pistol smaller ones.
        self.size = 1.3 * float(np.sqrt(self.length * self.height))

        # Pieces: shells welded by position over every texture's triangles.
        all_tris = np.concatenate([t for ts in mesh.tris.values() for t in ts])
        key = np.round(mesh.pos / 1e-4).astype(np.int64)
        _, weld = np.unique(key, axis=0, return_inverse=True)
        weld = weld.ravel()
        comp_of_vertex = _components(weld[all_tris], int(weld.max()) + 1)[weld]
        tri = layout["tri"]
        self.piece = np.where(tri >= 0, comp_of_vertex[own[np.maximum(tri, 0), 0]], -1)
        counts = np.bincount(comp_of_vertex[all_tris[:, 0]], minlength=int(comp_of_vertex.max()) + 1)
        biggest = int(counts.argmax())
        self.role = np.full(self.piece.shape, BODY, np.int32)
        self.along = np.zeros(self.piece.shape, np.float32)  # metres along the piece's long axis
        self.t = np.zeros(self.piece.shape, np.float32)  # 0..1 along it (barrels: rear to muzzle; hanging: up)
        self.across = np.zeros(self.piece.shape, np.float32)  # metres across it, from its middle
        self.lift = np.zeros(self.piece.shape, np.float32)  # how far the surface faces up, square to the piece's axis
        # Metres around the piece's long axis from its top (the seam underneath): barrels and sleeves are wrapped
        # around, not projected from the side.
        self.around = np.zeros(self.piece.shape, np.float32)
        self.extent = np.zeros(self.piece.shape, np.float32)  # the piece's length along its long axis (metres)
        body_v = np.unique(all_tris[comp_of_vertex[all_tris[:, 0]] == biggest])
        z_body = gz[body_v]
        self.zc = float((z_body.min() + z_body.max()) / 2)
        self.width = float(z_body.max() - z_body.min())
        for cid in np.unique(self.piece[self.piece >= 0]):
            verts = mesh.pos[np.unique(all_tris[comp_of_vertex[all_tris[:, 0]] == cid])]
            c = (verts.min(0) + verts.max(0)) / 2
            axes = np.linalg.svd(verts - c, full_matrices=False)[2] if len(verts) > 2 else np.eye(3, dtype=np.float32)
            ext = np.ptp((verts - c) @ axes.T, axis=0)
            e0, e1 = axes[0], axes[1]
            low = (c @ U - self.y0) / self.height < 0.3
            role = SLEEVE if sleeves else BODY
            if cid != biggest and not sleeves:
                if ext[0] < 0.06 * self.length:
                    role = SMALL
                elif abs(e0 @ F) > 0.85 and ext[0] > 2.5 * ext[1]:
                    role = BARREL
                    e0 = e0 if e0 @ F > 0 else -e0
                elif (abs(e0 @ U) > 0.55 and ext[0] > 1.4 * ext[1]) or low:  # magazines, grips
                    role = HANG
                    e0 = axes[int(np.argmax(np.abs(axes @ U)))]
                    e0 = e0 if e0 @ U > 0 else -e0
            m = self.piece == cid
            rel = P[m] - c
            a = rel @ e0
            ends = (verts - c) @ e0
            lo, hi = float(ends.min()), float(ends.max())
            self.role[m] = role
            self.along[m] = a
            self.t[m] = (a - lo) / max(hi - lo, 1e-6)
            self.extent[m] = hi - lo
            self.across[m] = rel @ e1
            up_sq = U - (U @ e0) * e0
            up_sq = up_sq / max(float(np.linalg.norm(up_sq)), 1e-6)
            self.lift[m] = N[m] @ up_sq
            perp = rel - (rel @ e0)[:, None] * e0
            radius = float(np.median(np.linalg.norm(perp, axis=1))) if len(perp) else 0.0
            self.around[m] = np.arctan2(perp @ np.cross(e0, up_sq), perp @ up_sq) * radius
        self.F, self.U, self.S = F, U, S

    @staticmethod
    def _step(P, ax):
        fwd = np.roll(P, -1, ax) - P
        bwd = P - np.roll(P, 1, ax)
        pick = (np.linalg.norm(fwd, axis=2) <= np.linalg.norm(bwd, axis=2))[..., None]
        return np.where(pick, fwd, bwd)

    def fw_of(self, f) -> np.ndarray:
        """The change per texel of any field f (h x w), not counting jumps to another island."""
        step = 0.0
        for ax in (0, 1):
            step = step + np.minimum(np.abs(np.roll(f, -1, ax) - f), np.abs(f - np.roll(f, 1, ax))) ** 2
        return np.sqrt(step) + 1e-7

    def fw(self, g) -> np.ndarray:
        """The change per texel of the linear function P . g (g a direction, constant or per texel)."""
        g = np.asarray(g, np.float32)
        return np.hypot((self.Ji * g).sum(-1), (self.Jj * g).sum(-1)) + 1e-7

    def mask(self, role: int) -> np.ndarray:
        return self.cover & (self.role == role)

    def plane(self):
        """The flat coordinates (metres) a motif is drawn in on each texel's face: (forward, up) on the sides,
        (forward, side) on top and bottom, (side, up) on the ends."""
        a = np.where(self.end, self.z, self.x)
        b = np.where(self.top, self.z, self.y)
        return a, b


def _components(tris: np.ndarray, n: int) -> np.ndarray:
    """The connected piece of each vertex (union-find over the triangles' edges)."""
    parent = np.arange(n)

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for a, b, c in tris:
        ra, rb, rc = find(a), find(b), find(c)
        parent[rb] = ra
        parent[rc] = ra
    return np.array([find(i) for i in range(n)])


def _line_cov(g: _Gun, f, at: float, hw: float, direction):
    """Coverage of a line of half width hw (metres) where the linear function f (along direction) equals at."""
    return np.clip((hw - np.abs(f - at)) / g.fw(direction) + 0.5, 0.0, 1.0)


def _below(g: _Gun, f, at, direction):
    """Coverage of f < at, anti-aliased (at may vary per texel)."""
    return np.clip((at - f) / g.fw(direction) + 0.5, 0.0, 1.0)


def _sweep(g: _Gun, lo: float, hi: float, rising_to_front: bool):
    """A slanted split line across the gun (metres up), from lo of the gun's height at one end to hi at the other, and
    the direction its edge is measured in (up, tipped by the slant)."""
    k = (hi - lo) * g.height / g.length
    if rising_to_front:
        yb = g.y0 + g.height * lo + k * (g.x - g.x0)
    else:
        yb = g.y0 + g.height * hi - k * (g.x - g.x0)
        k = -k
    direction = g.U - k * g.F
    direction = direction / np.linalg.norm(direction)
    # The distance to the line, measured square to it.
    return yb, direction, 1.0 / np.sqrt(1.0 + k * k)


# ---------------------------------------------------------------------------------------------------------------------
# Kaido: Japanese racing livery

def _glyph_plate(g: _Gun, out, rng, cx, cy, pw, ph, plate_col, ink_col, on):
    """A decal plate (pw x ph metres) centred at (cx, cy) on the texels on (side faces) carrying rows of
    katakana-like strokes."""
    a, b = g.x, g.y
    near = on & (np.abs(a - cx) < pw) & (np.abs(b - cy) < ph)
    if not near.any():
        return out
    X, Y = a[near] - cx, b[near] - cy
    px = g.mpp[near]
    if plate_col is not None:
        cov = _cov(_sd_box(X, Y, pw / 2, ph / 2, ph * 0.12), px)
        out[near] = _mix(out[near], plate_col, cov)
        rim = np.clip((ph * 0.035 - np.abs(_sd_box(X, Y, pw / 2 - ph * 0.14, ph / 2 - ph * 0.14, ph * 0.05))) / px + 0.5,
                      0.0, 1.0)
        out[near] = _mix(out[near], ink_col, rim)
    cell = ph * 0.48
    t = cell * 0.08
    d = np.full(X.shape, 1.0, np.float32)
    gx = -pw / 2 + cell * 0.75
    while gx + cell <= pw / 2 - cell * 0.5:
        gy = -cell / 2
        for _s in range(2 + int(rng.integers(0, 2))):
            kind, k = int(rng.integers(0, 4)), float(rng.random())
            if kind == 0:
                seg = (gx, gy + t + k * (cell - 2 * t), gx + cell, gy + t + k * (cell - 2 * t))
            elif kind == 1:
                seg = (gx + t + k * (cell - 2 * t), gy, gx + t + k * (cell - 2 * t), gy + cell)
            elif kind == 2:
                seg = (gx + cell - t, gy + cell - t, gx + cell * 0.45 * k, gy + t)
            else:
                seg = (gx + cell * (0.2 + 0.5 * k), gy + cell * 0.85, gx + cell * (0.35 + 0.5 * k), gy + cell * 0.55)
            d = np.minimum(d, _sd_segment(X, Y, *seg))
        gx += cell * 1.3
    out[near] = _mix(out[near], ink_col, np.clip((t - d) / px + 0.5, 0.0, 1.0))
    return out


def _hazard(g: _Gun, period: float):
    """Yellow-and-black hatching, slanted on every face (the sum of the gun's three coordinates): the hatch value
    (0..1) per texel."""
    k = g.F + g.U + g.S
    s = (g.x + g.y + g.z) / period
    return _stripe(s, 0.5, g.fw(k) / period)


def _livery(g: _Gun, colours, rng) -> np.ndarray:
    red, navy, white, yellow, black = (_rgb(c) for c in colours)
    h, w = g.cover.shape
    out = np.empty((h, w, 3), np.float32)
    out[:] = red
    L = g.size
    ft = g.fw_of(g.t)

    # Body: red over navy, split by a line sweeping up toward the stock, edged in white, with a second pinstripe above.
    body = g.mask(BODY)
    yb, ydir, sq = _sweep(g, 0.34, 0.58, rising_to_front=False)
    d = (g.y - yb) * sq  # metres above the split, square to it
    navy_cov = _below(g, d, 0.0, ydir)
    paint = _mix(np.broadcast_to(red, out.shape), navy, navy_cov)
    paint = _mix(paint, white, _line_cov(g, d, 0.0, 0.006 * L, ydir))
    paint = _mix(paint, white, _line_cov(g, d, 0.022 * L, 0.0022 * L, ydir))
    # Twin white racing stripes over the top and down the ends, above the split.
    zd = np.abs(g.z - g.zc)
    over = (g.top | g.end) & (d > 0)
    sw = max(0.1 * g.width, 0.004 * L)
    gap = max(0.07 * g.width, 0.003 * L)
    stripes = np.maximum(_span(zd, gap, gap + sw, g.fw(g.S)), _span(zd, gap + 1.4 * sw, gap + 1.9 * sw, g.fw(g.S)))
    paint = _mix(paint, white, stripes * over)
    out[body] = paint[body]
    # Decal plates on the sides of the body: white plates with navy glyphs, and a row of white glyphs on the red.
    on = body & g.side & (d > 0.02 * L)
    if on.any():
        ys = g.y[on]
        cy = float(np.percentile(ys, 60))
        for k, (cx, plate) in enumerate(((0.30, True), (0.52, False), (0.18, False))):
            x = g.x0 + cx * g.length
            out = _glyph_plate(g, out, rng, x, cy - 0.01 * L * k, 0.13 * L, 0.04 * L,
                               white if plate else None, navy if plate else white, on)

    # Barrels: black, a red band at the breech, a white ring and a hazard ring near the muzzle.
    barrel = g.mask(BARREL)
    paint = np.broadcast_to(black, out.shape).copy()
    paint = _mix(paint, red, _span(g.t, 0.0, 0.14, ft))
    paint = _mix(paint, white, _span(g.t, 0.74, 0.77, ft))
    paint = _mix(paint, _mix(np.broadcast_to(black, out.shape), yellow, _hazard(g, 0.02 * L)),
                 _span(g.t, 0.82, 0.94, ft))
    out[barrel] = paint[barrel]

    # Hanging pieces (magazines, grips): navy with a white pinstripe down the middle and a hazard foot.
    hang = g.mask(HANG)
    paint = np.broadcast_to(navy, out.shape).copy()
    paint = _mix(paint, white, np.clip((0.006 * L - np.abs(g.across)) / g.mpp + 0.5, 0.0, 1.0) * ~g.end)
    paint = _mix(paint, red, _span(g.t, 0.80, 1.01, ft))
    paint = _mix(paint, _mix(np.broadcast_to(black, out.shape), yellow, _hazard(g, 0.016 * L)),
                 _span(g.t, -0.01, 0.16, ft))
    out[hang] = paint[hang]

    # Small parts: black.
    out[g.mask(SMALL)] = black

    # Sleeves: a navy racing suit, a red stripe edged in white along the top of each arm, red cuffs.
    sleeve = g.mask(SLEEVE)
    if sleeve.any():
        fl, ft = g.fw_of(g.lift), g.fw_of(g.t)
        paint = np.broadcast_to(navy, out.shape).copy()
        paint = _mix(paint, white, _span(g.lift, 0.70, 2.0, fl))
        paint = _mix(paint, red, _span(g.lift, 0.80, 2.0, fl))
        cuffs = np.maximum(_span(g.t, -1.0, 0.12, ft), _span(g.t, 0.88, 2.0, ft))
        paint = _mix(paint, white, np.maximum(_span(g.t, -1.0, 0.14, ft), _span(g.t, 0.86, 2.0, ft)))
        paint = _mix(paint, red, cuffs)
        out[sleeve] = paint[sleeve]
    return out


# ---------------------------------------------------------------------------------------------------------------------
# Cryo: clean sci-fi panels

def _cryo(g: _Gun, colours, rng) -> np.ndarray:
    white, black, deep, sky = (_rgb(c) for c in colours)
    h, w = g.cover.shape
    out = np.empty((h, w, 3), np.float32)
    out[:] = white
    L = g.size
    ft = g.fw_of(g.t)

    # Body: white over black, a blue strip between them (deep below to sky above, a white pinstripe in it) sweeping up
    # toward the muzzle.
    body = g.mask(BODY)
    yb, ydir, sq = _sweep(g, 0.38, 0.52, rising_to_front=True)
    d = (g.y - yb) * sq
    band = 0.05 * L
    paint = np.broadcast_to(white, out.shape).copy()
    blue = _mix(np.broadcast_to(deep, out.shape), sky, np.clip(d / band, 0.0, 1.0))
    paint = _mix(paint, blue, _below(g, d, band, ydir))
    paint = _mix(paint, black, _below(g, d, 0.0, ydir))
    paint = _mix(paint, white, _line_cov(g, d, 0.62 * band, 0.05 * band, ydir))
    # Black seams at the strip's edges, a light lip on the white side.
    paint = _mix(paint, black, _line_cov(g, d, band, 0.0018 * L, ydir))
    paint = _mix(paint, white * 0.82, _line_cov(g, d, band + 0.0045 * L, 0.0012 * L, ydir))
    # Panel cuts square to the gun on the sides and top (dark on white, light on black), each with a light line beside.
    cuts = g.x0 + g.length * np.cumsum(0.07 + 0.08 * rng.random(16))
    face = ~g.end & ((d > band + 0.006 * L) | (d < -0.004 * L))
    for cx in cuts[cuts < g.x1]:
        cut = _line_cov(g, g.x, cx, 0.0012 * L, g.F) * face
        paint = _mix(paint, np.where((d > 0)[..., None], black, white * 0.55), 0.85 * cut)
        paint = _mix(paint, white, 0.3 * _line_cov(g, g.x, cx + 0.004 * L, 0.0008 * L, g.F) * face * (d > 0))
    # A sky-blue stripe along the top, edged in white and black.
    zd = np.abs(g.z - g.zc)
    sw = max(0.16 * g.width, 0.006 * L)
    top = g.top & (d > band)
    paint = _mix(paint, black, _span(zd, -1.0, sw + 0.0035 * L, g.fw(g.S)) * top)
    paint = _mix(paint, sky, _span(zd, -1.0, sw, g.fw(g.S)) * top)
    paint = _mix(paint, white, _span(zd, 0.35 * sw, 0.5 * sw, g.fw(g.S)) * top)
    # Rivets along both edges of the strip, on the sides.
    sp = 0.03 * L
    r = 0.0055 * L
    rx = (g.x - g.x0) % sp - sp / 2
    for row in (-0.012 * L, band + 0.012 * L):
        cov, shade, spec = _dome(rx, -(d - row), r, g.mpp)
        steel = np.clip(_mix(white, black, 0.45) * shade[..., None] + 0.4 * spec[..., None], 0, 1)
        paint = _mix(paint, black, 0.4 * _cov(np.hypot(rx - 0.15 * r, d - row + 0.2 * r) - 1.15 * r, g.mpp) * g.side)
        paint = _mix(paint, steel, cov * g.side)
    # Chevrons pointing to the muzzle along the strip, in white.
    a = (g.x - g.x0) / (0.6 * band)
    for k, cx in enumerate((0.2, 0.62)):
        base = cx * g.length / (0.6 * band)
        dch = np.full(g.x.shape, 1.0, np.float32)
        for j in range(3):
            dch = np.minimum(dch, _sd_chevron((d - 0.3 * band) / (0.6 * band), -(a - base - 1.1 * j)))
        paint = _mix(paint, white, 0.9 * _cov(dch * 0.6 * band, g.mpp) * g.side)
    out[body] = paint[body]

    # Barrels: black, white cooling rings at the breech, a deep and a sky ring at the muzzle.
    barrel = g.mask(BARREL)
    paint = np.broadcast_to(black, out.shape).copy()
    fins = _stripe(g.along / (0.016 * L), 0.35, g.mpp * 1.5 / (0.016 * L)) * _span(g.t, 0.08, 0.42, ft)
    paint = _mix(paint, white, fins)
    paint = _mix(paint, deep, _span(g.t, 0.84, 0.9, ft))
    paint = _mix(paint, sky, _span(g.t, 0.92, 1.01, ft))
    out[barrel] = paint[barrel]

    # Hanging pieces: deep blue at the foot to sky at the top, white ribs across and a black foot plate.
    hang = g.mask(HANG)
    paint = _mix(np.broadcast_to(deep, out.shape), sky, np.clip(g.t, 0.0, 1.0))
    ribs = _stripe(g.along / (0.014 * L), 0.3, g.mpp * 1.5 / (0.014 * L)) * _span(g.t, 0.3, 0.8, ft)
    paint = _mix(paint, white, ribs * ~g.end)
    paint = _mix(paint, black, _span(g.t, -0.01, 0.12, ft))
    out[hang] = paint[hang]

    # Small parts: sky-blue accents.
    out[g.mask(SMALL)] = sky

    # Sleeves: white over black (split along each arm's side), a blue strip between them, sky-blue cuffs.
    sleeve = g.mask(SLEEVE)
    if sleeve.any():
        fl, ft = g.fw_of(g.lift), g.fw_of(g.t)
        paint = np.broadcast_to(white, out.shape).copy()
        paint = _mix(paint, _mix(np.broadcast_to(deep, out.shape), sky, np.clip((g.lift + 0.25) / 0.25, 0.0, 1.0)),
                     _span(g.lift, -2.0, 0.0, fl))
        paint = _mix(paint, black, _span(g.lift, -2.0, -0.25, fl))
        ends = np.maximum(_span(g.t, -1.0, 0.1, ft), _span(g.t, 0.9, 2.0, ft))
        paint = _mix(paint, black, np.maximum(_span(g.t, -1.0, 0.12, ft), _span(g.t, 0.88, 2.0, ft)))
        paint = _mix(paint, sky, ends)
        out[sleeve] = paint[sleeve]
    return out


# ---------------------------------------------------------------------------------------------------------------------
# Pyre: hot-rod flames sweeping back from the muzzle

def _tongues(g: _Gun, dist, lateral, band: float, reach: float, rng):
    """Flame tongues growing from dist = 0 (metres) along dist, side by side across lateral (metres, one tongue every
    band): each starts as a solid front, narrows and ends in a point between half of reach and reach (reach may vary
    per texel). Returns the
    signed distance to the flames' edge (metres, positive inside) and how far along its tongue each point is (0..1)."""
    lengths = rng.uniform(0.45, 1.0, 64).astype(np.float32)
    q = lateral / band
    k = np.floor(q).astype(np.int64)
    f = q - k
    tip = lengths[k % 64] * reach
    base = 0.22 * reach
    edge = base + (tip - base) * (1.0 - np.abs(2.0 * f - 1.0)) ** 1.6
    field = edge - dist
    # Metres from the edge: the field over its change per metre.
    dm = field * g.mpp / np.maximum(g.fw_of(field), 1e-7)
    return dm, np.clip(dist / np.maximum(edge, 1e-6), 0.0, 1.0)


def _fire(g: _Gun, dm, along, colours, black, line: float):
    """Flames over black from _tongues: pale at the root to red at the tips, a dark-red pinstripe round the edge."""
    ramp = np.stack([_rgb(c) for c in colours[2:]][::-1])  # pale core first
    t = np.clip(along, 0.0, 1.0) * (len(ramp) - 1)
    i = np.clip(t.astype(np.int32), 0, len(ramp) - 2)
    fire = ramp[i] * (1 - (t - i))[..., None] + ramp[i + 1] * (t - i)[..., None]
    out = _mix(np.broadcast_to(black, fire.shape), fire, np.clip(dm / g.mpp + 0.5, 0.0, 1.0))
    return _mix(out, _rgb(colours[1]), np.clip((line - np.abs(dm)) / g.mpp + 0.5, 0.0, 1.0))


def _pyre(g: _Gun, colours, rng, luma=None) -> np.ndarray:
    black = _rgb(colours[0])
    h, w = g.cover.shape
    out = np.empty((h, w, 3), np.float32)
    out[:] = black
    L = g.size
    line = 0.0035 * L

    # Body (and anything hanging off it): wavy tongues from the muzzle end sweeping back and up toward the stock, with
    # a slow S-curl.
    dist = g.x1 - g.x
    lateral = g.y - 0.32 * dist + 0.02 * L * np.sin(dist / (0.18 * g.length) * 2 * np.pi)
    dm, along = _tongues(g, dist, lateral, 0.075 * L, 0.85 * g.length, rng)
    paint = _fire(g, dm, along, colours, black, line)
    body = g.mask(BODY)
    out[body] = paint[body]

    # Hanging pieces (magazines, grips): their own flames licking up from the foot.
    hang = g.mask(HANG)
    dm, along = _tongues(g, g.t * g.extent, g.across, 0.05 * L, 0.8 * np.maximum(g.extent, 1e-3), rng)
    paint = _fire(g, dm, along, colours, black, line)
    out[hang] = paint[hang]

    # Barrels: blued steel, heat-tinted toward the muzzle (straw, bronze, purple, blue), a flame-orange ring at the
    # breech.
    barrel = g.mask(BARREL)
    ft = g.fw_of(g.t)
    tint = np.stack([black, np.array([0.62, 0.48, 0.2]), np.array([0.5, 0.26, 0.12]), np.array([0.34, 0.12, 0.36]),
                     np.array([0.12, 0.22, 0.52])]).astype(np.float32)
    s = np.clip((g.t - 0.5) / 0.5, 0.0, 1.0) * (len(tint) - 1)
    i = np.clip(s.astype(np.int32), 0, len(tint) - 2)
    paint = tint[i] * (1 - (s - i))[..., None] + tint[i + 1] * (s - i)[..., None]
    paint = _mix(paint, _rgb(colours[3]), _span(g.t, 0.02, 0.06, ft))
    out[barrel] = paint[barrel]

    # Small parts: flame orange.
    out[g.mask(SMALL)] = _rgb(colours[3])

    # Sleeves: black, flames climbing from each cuff.
    sleeve = g.mask(SLEEVE)
    if sleeve.any():
        ext = np.maximum(g.extent, 1e-3)
        dm, along = _tongues(g, np.minimum(g.t, 1.0 - g.t) * ext, g.around, 0.03 * ext, 0.3 * ext, rng)
        paint = _fire(g, dm, along, colours, black, 0.0015 * ext)
        out[sleeve] = paint[sleeve]
    return out


# ---------------------------------------------------------------------------------------------------------------------
# Every other skin: its flat pattern laid on the gun

# Metres of gun one tile of a flat pattern covers: the same on every gun, so a camo is one scale across the arsenal.
TILE_M = 0.40
TILE_PX = 1024
# Shapes drawn bigger or smaller than one tile per TILE_M.
_TILE_SCALE = {"pixel": 1.3, "blotch": 1.5, "blotch-soft": 1.5, "scanlines": 1.6, "stripe": 1.2}


@functools.lru_cache(maxsize=4)
def _tile(pattern: str, shape: str, colours: tuple) -> np.ndarray:
    """The pattern drawn flat (TILE_PX square, wrapping), weathering included: one per pattern, shared by every gun
    texture so a magazine on its own texture carries on the gun's pattern."""
    from . import cosmetics  # noqa: PLC0415

    n = TILE_PX
    flat = np.full((n, n), 0.5, np.float32)
    if shape in ("gold", "mastercraft"):
        weight = cosmetics.MASTERCRAFT_WEIGHT if shape == "mastercraft" else 1.0
        groove, raised = cosmetics._engraving(cosmetics._rng(pattern, "gun"), n, n, shape == "mastercraft", weight)
        return np.stack([groove, raised, np.zeros_like(groove)], -1).astype(np.float32)
    rgb = np.array(cosmetics._SHAPES[shape](cosmetics._rng(pattern, "gun"), n, n, list(colours), flat), np.float32)
    if shape in cosmetics._WEATHERED:
        cosmetics._fine_detail(cosmetics._rng(pattern, "gun:detail"), rgb)
    return rgb


def _sample(img: np.ndarray, u, v) -> np.ndarray:
    """img (wrapping) at (u, v) in tile units, bilinear."""
    n, m = img.shape[:2]
    x, y = u * m - 0.5, v * n - 0.5
    x0, y0 = np.floor(x), np.floor(y)
    fx, fy = (x - x0)[..., None].astype(np.float32), (y - y0)[..., None].astype(np.float32)
    x0, y0 = x0.astype(np.int64) % m, y0.astype(np.int64) % n
    x1, y1 = (x0 + 1) % m, (y0 + 1) % n
    top = img[y0, x0] * (1 - fx) + img[y0, x1] * fx
    bottom = img[y1, x0] * (1 - fx) + img[y1, x1] * fx
    return top * (1 - fy) + bottom * fy


def _place(g: _Gun, tile_m: float):
    """Each texel's place on the flat pattern (tile units, image up = the gun's up): the sides take it as seen from the
    side, the top and bottom from above, the ends from the front (each shifted so it does not echo the side), and
    barrels and sleeves wrapped round their axis (the seam underneath)."""
    side_u, side_v = (g.x - g.x0) / tile_m, (g.y1 - g.y) / tile_m + 0.02
    u = np.where(g.top, side_u + 0.37, np.where(g.end, (g.z - g.zc) / tile_m + 0.71, side_u))
    v = np.where(g.top, (g.z - g.zc) / tile_m + 0.5, side_v)
    wrapped = g.cover & ((g.role == BARREL) | (g.role == SLEEVE))
    u = np.where(wrapped & (g.role == SLEEVE), g.along / tile_m, u)
    v = np.where(wrapped, g.around / tile_m + 0.5, v)
    return u, v


def _noise3(rng, X, Y, Z, cell: float) -> np.ndarray:
    """3D value noise (cells of cell metres, smooth-stepped between lattice points) at the points X, Y, Z (metres)."""
    n = 64
    lat = rng.random((n, n, n), dtype=np.float32)
    out = np.zeros(X.shape, np.float32)
    co = [c / cell for c in (X, Y, Z)]
    i = [np.floor(c).astype(np.int64) for c in co]
    f = [(c - k).astype(np.float32) for c, k in zip(co, i)]
    f = [t * t * (3.0 - 2.0 * t) for t in f]
    for dx in (0, 1):
        wx = f[0] if dx else 1.0 - f[0]
        for dy in (0, 1):
            wy = f[1] if dy else 1.0 - f[1]
            for dz in (0, 1):
                wz = f[2] if dz else 1.0 - f[2]
                out += wx * wy * wz * lat[(i[0] + dx) % n, (i[1] + dy) % n, (i[2] + dz) % n]
    return out


def _fbm3(rng, X, Y, Z, cell: float, octaves: int) -> np.ndarray:
    out = np.zeros(X.shape, np.float32)
    amp = 1.0
    for _ in range(octaves):
        out += amp * _noise3(rng, X, Y, Z, cell)
        amp *= 0.5
        cell /= 2
    lo, hi = np.percentile(out, (1, 99))
    return np.clip((out - lo) / max(float(hi - lo), 1e-6), 0.0, 1.0)


def _solid_blotch(pattern: str, colours, P: np.ndarray, cover: np.ndarray, tile_m: float, soft: bool) -> np.ndarray:
    """The blotch camos (cosmetics._shape_blotch: warped layered noise cut into hard colour bands, desert's softened
    with a sand speckle) computed in 3D at each texel's point P (h x w x 3, metres): one solid camo the surface cuts
    through, with no seams and no views to blend. Cells are measured in tiles of tile_m metres, as the flat shape's are
    in textures."""
    from . import cosmetics  # noqa: PLC0415

    rng = cosmetics._rng(pattern, "solid")
    h, w = cover.shape
    X, Y, Z = (P[..., k][cover] for k in range(3))
    cells = lambda k: tile_m / k  # noqa: E731
    warp = [(_fbm3(rng, X, Y, Z, cells(6), 3) - 0.5) * 0.06 * tile_m for _ in range(3)]
    Xw, Yw, Zw = X + warp[0], Y + warp[1], Z + warp[2]
    value = _fbm3(rng, Xw, Yw, Zw, cells(3 if soft else 4), 4)
    value = value + (_noise3(rng, X, Y, Z, cells(64)) - 0.5) * (0.05 if soft else 0.10)
    value = value + (_noise3(rng, X, Y, Z, cells(160)) - 0.5) * (0.02 if soft else 0.05)
    lo, hi = np.percentile(value, (1, 99))
    full = np.zeros((h, w), np.float32)
    full[cover] = np.clip((value - lo) / max(float(hi - lo), 1e-6), 0.0, 1.0)
    # Bands, anti-aliased by the change per texel (not counting jumps to another island).
    step = 0.0
    for ax in (0, 1):
        step = step + np.minimum(np.abs(np.roll(full, -1, ax) - full), np.abs(full - np.roll(full, 1, ax))) ** 2
    fw = np.sqrt(step) + (0.07 if soft else 0.0) + 1e-6
    pal = np.stack([_rgb(c) for c in colours])
    out = np.empty((h, w, 3), np.float32)
    out[:] = pal[0]
    for k in range(1, len(colours)):
        out += (pal[k] - pal[k - 1]) * np.clip((full - k / len(colours)) / fw + 0.5, 0.0, 1.0)[..., None]
    tint = np.ones((h, w), np.float32)
    tint[cover] = 0.96 + 0.08 * _noise3(rng, X, Y, Z, cells(40))
    out *= tint[..., None]
    if soft:
        g1, g2 = np.zeros((h, w), np.float32), np.zeros((h, w), np.float32)
        g1[cover], g2[cover] = _noise3(rng, X, Y, Z, cells(520)), _noise3(rng, X, Y, Z, cells(900))
        out = _mix(out, pal[-1] * 1.06, 0.55 * cosmetics._ss(0.72, 0.84, g1))
        out = _mix(out, pal[0] * 0.9, 0.45 * cosmetics._ss(0.28, 0.16, g2) * cover)
    cosmetics._fine_detail(cosmetics._rng(pattern, "solid:detail"), out)
    return out


_SOLID = ("blotch", "blotch-soft")


def _stars(rng, X, Y, Z, cell: float, keep: float, r_lo: float, r_hi: float) -> np.ndarray:
    """Point stars scattered through space (one in about keep of the cells of cell metres, each at a random spot of
    its cell with a radius between r_lo and r_hi metres): each point's brightness 0..1."""
    table = rng.random((5, 1 << 16), dtype=np.float32)
    ix, iy, iz = (np.floor(c / cell).astype(np.int64) for c in (X, Y, Z))
    k = ((ix * 73856093) ^ (iy * 19349663) ^ (iz * 83492791)) & 0xFFFF
    cx, cy, cz = ((i + 0.2 + 0.6 * table[n][k]) * cell for n, i in enumerate((ix, iy, iz)))
    r = r_lo + (r_hi - r_lo) * table[3][k] ** 3
    d2 = (X - cx) ** 2 + (Y - cy) ** 2 + (Z - cz) ** 2
    on = table[4][k] < keep
    return np.where(on, np.exp(-d2 / (r * r)), 0.0).astype(np.float32)


def solid_galaxy(seed: str, colours, P: np.ndarray, cover: np.ndarray) -> np.ndarray:
    """Galaxy (OpenSCCT's own): a Doppler-style nebula, deep space swirling into blue, violet and pink ribbons, sown
    with stars, computed in 3D at each point P (metres) so it has no seams. colours: space, then the nebula dark to
    light, the last the stars' tint."""
    from . import cosmetics  # noqa: PLC0415

    rng = cosmetics._rng(seed, "galaxy")
    h, w = cover.shape
    X0, Y0, Z0 = (P[..., k][cover] for k in range(3))
    X, Y, Z = X0, Y0, Z0
    # Two rounds of warping swirl the cloud into ribbons.
    for amount, cell in ((0.22, 0.3), (0.05, 0.1)):
        wx, wy, wz = ((_fbm3(rng, X, Y, Z, cell, 3) - 0.5) * amount for _ in range(3))
        X, Y, Z = X + wx, Y + wy, Z + wz
    cloud = _fbm3(rng, X, Y, Z, 0.24, 5)
    ribbon = 1.0 - np.abs(2.0 * _fbm3(rng, X, Y, Z, 0.16, 4) - 1.0)
    t = np.clip(0.9 * cloud ** 2.2 + 0.5 * ribbon ** 6 - 0.04, 0.0, 1.0)
    pal = np.stack([_rgb(c) for c in colours[:-1]])
    s = t * (len(pal) - 1)
    i = np.clip(s.astype(np.int32), 0, len(pal) - 2)
    col = pal[i] * (1 - (s - i))[:, None] + pal[i + 1] * (s - i)[:, None]
    # Stars: a fine dust and a few bigger ones, dimmer where the nebula is bright.
    star = _stars(rng, X0, Y0, Z0, 0.018, 0.5, 0.0012, 0.0026)
    star = np.maximum(star, _stars(rng, X0 + 5.0, Y0, Z0, 0.07, 0.6, 0.002, 0.006))
    star = star * (0.4 + 0.6 * (1.0 - t))
    col = col + (_rgb(colours[-1]) - col) * np.clip(star * 1.3, 0.0, 1.0)[:, None]
    out = np.zeros((h, w, 3), np.float32)
    out[cover] = col
    return out


def solid_inferno(seed: str, colours, P: np.ndarray, cover: np.ndarray, dist: np.ndarray, reach,
                  upright: np.ndarray) -> np.ndarray:
    """Inferno (OpenSCCT's own): flames over black, rising dist metres from their source (the boots, the hands) and
    licking out to reach metres, pale yellow at the root to red at the tips, with a dark-red edge. upright: where the
    flames climb along Y (else along X, the arms out sideways). Computed in 3D at each point P, so no seams. colours:
    the black, the dark red edge, then the flame red to pale yellow."""
    from . import cosmetics  # noqa: PLC0415

    rng = cosmetics._rng(seed, "inferno")
    h, w = cover.shape
    X, Y, Z = (P[..., k][cover] for k in range(3))
    d = dist[cover]
    up = upright[cover]
    reach = np.broadcast_to(np.asarray(reach, np.float32), cover.shape)[cover]
    # The noise is stretched along the way the flames climb, so it reads as tongues.
    along = np.where(up, Y, np.abs(X))
    Xs, Ys = np.where(up, X, along * 0.3), np.where(up, along * 0.3, Y)
    wx, wz = ((_fbm3(rng, Xs, Ys, Z, 0.09, 3) - 0.5) * 0.06 for _ in range(2))
    n = _fbm3(rng, Xs + wx, Ys, Z + wz, 0.07, 4)
    s = d / np.maximum(reach, 1e-3)  # 0 at the source, 1 at the furthest tips
    field = 1.5 * n - s - 0.15  # > 0 inside the flames
    black = _rgb(colours[0])
    ramp = np.stack([_rgb(c) for c in colours[2:]][::-1])  # pale core first
    heat = np.clip(s / np.maximum(1.5 * n - 0.15, 1e-3), 0.0, 1.0) * (len(ramp) - 1)
    i = np.clip(heat.astype(np.int32), 0, len(ramp) - 2)
    fire = ramp[i] * (1 - (heat - i))[:, None] + ramp[i + 1] * (heat - i)[:, None]
    # Edges anti-aliased by the field's change per texel (not counting jumps to another island).
    full = np.full((h, w), -1.0, np.float32)
    full[cover] = field
    step = 0.0
    for ax in (0, 1):
        step = step + np.minimum(np.abs(np.roll(full, -1, ax) - full), np.abs(full - np.roll(full, 1, ax))) ** 2
    fw = np.sqrt(step)[cover] + 1e-4
    inside = np.clip(field / fw + 0.5, 0.0, 1.0)
    col = black + (fire - black) * inside[:, None]
    line = np.clip((0.035 - np.abs(field)) / fw + 0.5, 0.0, 1.0) * np.clip((field + 0.06) / 0.06, 0.0, 1.0)
    col = col + (_rgb(colours[1]) - col) * line[:, None]
    out = np.zeros((h, w, 3), np.float32)
    out[cover] = col
    return out


def _tone(c) -> float:
    return float(c @ np.array([0.3, 0.59, 0.11], np.float32))


def _laid(g: _Gun, colours, rng, luma=None, *, pattern: str, shape: str) -> np.ndarray:
    """The pattern laid over the whole gun at one scale (_place), with trim on the parts: a dark muzzle with an accent
    ring on each barrel, a dark foot plate on magazines and grips, dark small parts, dark cuffs edged in the accent on
    the sleeves. The gold finishes stay gold all over (darker gold trim, plain polished small parts) and keep the
    retail brightness."""
    from . import cosmetics  # noqa: PLC0415

    tile_m = TILE_M * _TILE_SCALE.get(shape, 1.0)
    u, v = _place(g, tile_m)
    img = None if shape in _SOLID else _tile(pattern, shape, tuple(colours))
    pal = [_rgb(c) for c in colours]
    gilded = shape in ("gold", "mastercraft")
    if gilded:
        gr = _sample(img, u, v)
        lum = luma if luma is not None else np.full(g.cover.shape, 0.5, np.float32)
        depth = cosmetics.MASTERCRAFT_DEPTH if shape == "mastercraft" else 1.0
        out = cosmetics._gold_ramp(lum, colours, gr[..., 0], gr[..., 1], depth)
        plain = cosmetics._gold_ramp(lum, colours)
        metal = pal[0] * 0.75
        accent = pal[-1]
    else:
        if shape in _SOLID:
            P = np.stack([g.x, g.y, g.z], -1)
            out = _solid_blotch(pattern, colours, P, g.cover, tile_m, shape == "blotch-soft")
        else:
            out = _sample(img, u, v)
        darkest = min(pal, key=_tone)
        metal = (0.55 * darkest + 0.45 * np.full(3, 0.07, np.float32)).astype(np.float32)
        # The accent: the most colourful of the lighter colours.
        light = [c for c in pal if c is not darkest] or pal
        accent = max(light, key=lambda c: float(c.max() - c.min()) + 0.3 * float(c.max()))
    ft = g.fw_of(g.t)

    barrel = g.mask(BARREL)
    paint = _mix(out, metal, _span(g.t, 0.80, 2.0, ft))
    paint = _mix(paint, accent, _span(g.t, 0.80, 0.835, ft))
    out[barrel] = paint[barrel]

    hang = g.mask(HANG)
    paint = _mix(out, metal, _span(g.t, -1.0, 0.06, ft))
    paint = _mix(paint, accent, _span(g.t, 0.06, 0.075, ft))
    out[hang] = paint[hang]

    small = g.mask(SMALL)
    out[small] = plain[small] if gilded else metal

    sleeve = g.mask(SLEEVE)
    if sleeve.any():
        paint = _mix(out, metal, np.maximum(_span(g.t, -1.0, 0.09, ft), _span(g.t, 0.91, 2.0, ft)))
        paint = _mix(paint, accent, np.maximum(_span(g.t, 0.09, 0.105, ft), _span(g.t, 0.895, 0.91, ft)))
        out[sleeve] = paint[sleeve]
    return out


_DRAW = {"livery": _livery, "cryo": _cryo, "flames": _pyre}
# Shapes that are drawn on the body only (never on a gun texture).
_NOT_ON_GUNS = ("skeleton",)


def handles(shape: str, texture: str) -> bool:
    """Whether this shape is drawn on the gun for this texture (else it is drawn across the texture): every shape on
    every gun texture with a mesh."""
    return shape not in _NOT_ON_GUNS and texture in GUN_MESH


def draw(shape: str, colours: list[int], mesh: Mesh, texture: str, h: int, w: int, rng, pattern: str = "",
         luma: np.ndarray | None = None) -> np.ndarray:
    """The skin's colours (h x w x 3, 0..1) for a gun texture, drawn on the gun's mesh. luma (the retail brightness,
    h x w) is what the gold finishes follow."""
    _, forward, up, sleeves = GUN_MESH[texture]
    g = _Gun(mesh, texture, h, w, forward, up, sleeves)
    if shape in _DRAW:
        return _DRAW[shape](g, colours, rng)
    return _laid(g, colours, rng, luma, pattern=pattern or shape, shape=shape)


# ---------------------------------------------------------------------------------------------------------------------
# Suits: the same flat patterns laid on the body

# Shapes drawn at another size on the suits than on the guns.
_SUIT_SCALE = {"stripe": 2.0}
# Shapes whose stripes run across the tile's width: turned on the body so they wrap round it.
_ACROSS = ("stripe",)

def draw_suit(pattern: str, shape: str, colours: list[int], mesh: Mesh, texture: str, h: int, w: int,
              luma: np.ndarray | None = None) -> np.ndarray:
    """A camo (h x w x 3, 0..1) for a body texture, laid on the body in its bind pose at the guns' scale (TILE_M):
    seen from the front and back, from the sides and from above, blended by how far each texel faces each way, so it
    runs on across the atlas's seams at one size all over the suit. The gold finishes keep the retail brightness."""
    from . import cosmetics  # noqa: PLC0415

    layout = bake(mesh, texture, h, w)
    if layout is None:
        raise ValueError(f"{texture} is not a texture of the body's mesh")
    # The body's atlas shares islands between left and right and between front and back (a texel can be traced to
    # either side, triangle by triangle): the pattern is drawn on the body folded to one quarter, so every texel
    # agrees whichever side it shows on.
    P = np.abs(layout["P"]) * np.array([1.0, 0.0, 1.0], np.float32) + layout["P"] * np.array([0.0, 1.0, 0.0], np.float32)
    N = layout["N"]
    x, y, z = P[..., 0], P[..., 1], P[..., 2]
    tile_m = TILE_M * _SUIT_SCALE.get(shape, _TILE_SCALE.get(shape, 1.0))
    if shape in _SOLID:
        return _solid_blotch(pattern, colours, P, layout["cover"], tile_m, shape == "blotch-soft")
    if shape == "galaxy":
        return solid_galaxy(pattern, colours, P, layout["cover"])
    if shape == "inferno":
        # From the boots up the legs and body; from the hands along the arms (out sideways in the bind pose).
        arm = np.isin(layout["group"], (ARM_L, ARM_R, HAND_L, HAND_R))
        bottom, span = float(mesh.pos[:, 1].min()), float(np.abs(mesh.pos[:, 0]).max())
        dist = np.where(arm, span - np.abs(x), y - bottom)
        reach = np.where(arm, 0.55, 1.5)
        return solid_inferno(pattern, colours, P, layout["cover"], dist, reach, ~arm)
    img = _tile(pattern, shape, tuple(colours))
    top = float(mesh.pos[:, 1].max())
    down = (top - y) / tile_m
    fu, fv, su, sv = x / tile_m, down, z / tile_m + 0.37, down + 0.21
    if shape in _ACROSS:
        # Stripes run across each limb: turned to wrap round the torso, legs and head; the arms (out sideways in the
        # bind pose) keep them upright.
        flat = ~np.isin(layout["group"], (ARM_L, ARM_R, HAND_L, HAND_R))
        fu, fv = np.where(flat, down, fu), np.where(flat, x / tile_m, fv)
        su, sv = np.where(flat, down + 0.21, su), np.where(flat, z / tile_m + 0.37, sv)
    views = (_sample(img, fu, fv),  # front and back
             _sample(img, su, sv),  # sides
             _sample(img, x / tile_m + 0.61, z / tile_m + 0.43))  # above and below
    # Mostly one view per texel: blends only where the surface turns between them.
    wts = np.abs(N[..., [2, 0, 1]]) ** 8
    wts = wts / np.maximum(wts.sum(-1, keepdims=True), 1e-6)
    out = sum(v * wts[..., k:k + 1] for k, v in enumerate(views))
    if shape in ("gold", "mastercraft"):
        lum = luma if luma is not None else np.full((h, w), 0.5, np.float32)
        depth = cosmetics.MASTERCRAFT_DEPTH if shape == "mastercraft" else 1.0
        return cosmetics._gold_ramp(lum, colours, out[..., 0], out[..., 1], depth)
    return out
