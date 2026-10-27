"""Gun-shaped paint jobs for the painted gun skins (Kaido's racing livery, Cryo's sci-fi panels): drawn on the gun
itself, not across its texture.

A gun texture is a UV atlas of many small islands (a side of the receiver here, the top of the barrel there), so a
design drawn across the texture lands on each piece as a random slice of it. Instead, as for the skeleton camo, each
texel is traced back to the point of the gun's mesh it covers (skeleton.bake), and the design is drawn on the gun in
metres:

- the gun's frame: forward (to the muzzle), up and side, per mesh (GUN_MESH);
- its pieces: the mesh's separate shells (welded by position), each sorted by its shape into the body (receiver,
  stock, grip), barrels (long, thin and along the gun: barrels, handguards, scope tubes), hanging pieces (magazines,
  separate grips) and small parts (sights, levers, bolts);
- each texel's face: side, top or end, from its normal, which picks the plane a flat motif (decal, chevron, rivet) is
  drawn in.

The body takes the livery in the gun's frame (a two-tone split sweeping along the gun, pinstripes, stripes over the
top), barrels take rings along their own axis, hanging pieces their own colour along their length and small parts an
accent. So each part reads as painted, and a stripe carries on from one face to the next. The merc's sleeves (his
first-person arms share the gun's skin) are painted as a racing suit instead: a stripe along the top of each arm and
cuffs at its ends.

Shapes are measured against the gun's size (its length and height), so every gun gets the same design at its own
scale. Edges are
anti-aliased over about one texel, from how far the gun's surface moves per texel.
"""

from __future__ import annotations

import numpy as np

from .cosmetics import _cov, _dome, _mix, _rgb, _sd_box, _sd_chevron, _sd_segment, _span, _stripe
from .skeleton import Mesh, bake

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
        z_body = gz[np.unique(all_tris[comp_of_vertex[all_tris[:, 0]] == biggest])]
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
            self.across[m] = rel @ e1
            up_sq = U - (U @ e0) * e0
            up_sq = up_sq / max(float(np.linalg.norm(up_sq)), 1e-6)
            self.lift[m] = N[m] @ up_sq
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


_DRAW = {"livery": _livery, "cryo": _cryo}


def handles(shape: str, texture: str) -> bool:
    """Whether this shape is drawn on the gun for this texture (else it is drawn across the texture)."""
    return shape in _DRAW and texture in GUN_MESH


def draw(shape: str, colours: list[int], mesh: Mesh, texture: str, h: int, w: int, rng) -> np.ndarray:
    """The skin's colours (h x w x 3, 0..1) for a gun texture, drawn on the gun's mesh."""
    _, forward, up, sleeves = GUN_MESH[texture]
    g = _Gun(mesh, texture, h, w, forward, up, sleeves)
    return _DRAW[shape](g, colours, rng)
