"""The skeleton camo (OpenSCCT's own cosmetic): bones painted where they would lie on the body, not a repeating pattern.

The character textures are UV atlases, so a bone cannot be drawn in texture space directly. Instead each texel of a
body texture is traced back to the point of the bind pose (a T-pose, Y up, +X the character's left, +Z forward)
it covers, through the mesh's UVs (shared/chars/SPerso/<mesh>.glb), along with its normal and the body part it moves
with (from the skin weights). The bones are drawn as 2D shapes in metres, placed from the skeleton's joints:

- head, torso, arms and legs seen from the front (x, y) on texels facing forward and from behind on texels facing
  back; texels facing sideways fade to the suit;
- hands and feet seen from above (x, z).

Each body part only draws its own bones, so an arm in front of the ribs never takes a rib. The head is bone all over
(a skull), with sockets, nose and teeth on the front.

The swatch (no mesh) is the same drawing seen flat from the front.
"""

from __future__ import annotations

import json
import struct
from functools import lru_cache
from pathlib import Path

import numpy as np

_CT = {5126: np.float32, 5125: np.uint32, 5123: np.uint16, 5121: np.uint8}
_NC = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}

HEAD, TORSO, ARM_L, ARM_R, HAND_L, HAND_R, LEG_L, LEG_R, FOOT_L, FOOT_R = range(10)
_GROUPS = 10

AA = 0.0022  # edge blend, metres


def _group_of(name: str) -> int:
    """The body part a joint moves (names as the retail rig: 'B L Hand', 'Bip01 L UpperArm_RollBone')."""
    side = 0 if " L " in f" {name} " else 1
    low = name.lower()
    if "head" in low or "neck" in low or "casque" in low or low == "laser":
        return HEAD
    if "Hand" in name or "Finger" in name:
        return HAND_L if side == 0 else HAND_R
    if "UpperArm" in name or "ForeArm" in name:
        return ARM_L if side == 0 else ARM_R
    if "Thigh" in name or "Calf" in name:
        return LEG_L if side == 0 else LEG_R
    if "Foot" in name or "Toe" in name:
        return FOOT_L if side == 0 else FOOT_R
    return TORSO


class Mesh:
    """A skinned GLB's bind pose: vertices (position, normal, UV, body-part weights), each material's triangles and
    the joints' positions by name."""

    def __init__(self, path: Path):
        b = Path(path).read_bytes()
        n = struct.unpack_from("<I", b, 12)[0]
        j = json.loads(b[20:20 + n])
        off = 20 + n
        blen = struct.unpack_from("<I", b, off)[0]
        binary = b[off + 8:off + 8 + blen]

        def acc(i: int) -> np.ndarray:
            a = j["accessors"][i]
            v = j["bufferViews"][a["bufferView"]]
            dt, nc = _CT[a["componentType"]], _NC[a["type"]]
            size = np.dtype(dt).itemsize * nc
            stride = v.get("byteStride", 0) or size
            o = v.get("byteOffset", 0) + a.get("byteOffset", 0)
            raw = np.frombuffer(binary, np.uint8, (a["count"] - 1) * stride + size, o)
            rows = np.lib.stride_tricks.as_strided(raw, (a["count"], size), (stride, 1))
            return rows.copy().view(dt).reshape(a["count"], nc)

        skin = j["skins"][0]
        ibm = acc(skin["inverseBindMatrices"]).reshape(-1, 4, 4)
        names = [j["nodes"][k].get("name", "") for k in skin["joints"]]
        self.joints = {nm: np.linalg.inv(ibm[k].T)[:3, 3].astype(np.float32) for k, nm in enumerate(names)}
        joint_group = np.array([_group_of(nm) for nm in names])
        images = [im.get("uri", "") for im in j.get("images", [])]
        textures = j.get("textures", [])

        def texture_of(mat: int) -> str:
            m = j["materials"][mat]
            t = m.get("pbrMetallicRoughness", {}).get("baseColorTexture", {}).get("index")
            if t is None:
                return ""
            src = textures[t].get("source", t) if t < len(textures) else t
            return Path(images[src]).stem if src < len(images) else ""

        self.tris: dict[str, list[np.ndarray]] = {}
        prim0 = j["meshes"][0]["primitives"][0]["attributes"]
        self.pos = acc(prim0["POSITION"]).astype(np.float32)
        self.nrm = acc(prim0["NORMAL"]).astype(np.float32)
        self.uv = acc(prim0["TEXCOORD_0"]).astype(np.float32)
        jts = acc(prim0["JOINTS_0"]).astype(np.int64)
        wts = acc(prim0["WEIGHTS_0"]).astype(np.float32)
        wts /= np.maximum(wts.sum(1, keepdims=True), 1e-6)
        self.groups = np.zeros((len(self.pos), _GROUPS), np.float32)
        for k in range(jts.shape[1]):
            np.add.at(self.groups, (np.arange(len(self.pos)), joint_group[jts[:, k]]), wts[:, k])
        for mesh in j["meshes"]:
            for p in mesh["primitives"]:
                if p["attributes"]["POSITION"] != prim0["POSITION"] or "indices" not in p:
                    continue  # every retail character keeps one vertex list for all its materials
                self.tris.setdefault(texture_of(p.get("material", 0)), []).append(
                    acc(p["indices"]).reshape(-1, 3).astype(np.int64))
        vg = self.groups.argmax(1)
        if (vg == HEAD).any():
            self.head_top = float(self.pos[vg == HEAD, 1].max())
        elif "B Head" in self.joints:
            self.head_top = self.joints_y("B Head") + 0.2
        else:  # a gun: no head
            self.head_top = float(self.pos[:, 1].max())
        torso = self.pos[vg == TORSO]
        self._torso = torso

    def joints_y(self, name: str) -> float:
        return float(self.joints[name][1])

    def j(self, name: str) -> np.ndarray:
        return self.joints[name]

    def half_width(self, y: float) -> float:
        """The torso's half width at height y (its widest vertex within 3 cm)."""
        band = self._torso[np.abs(self._torso[:, 1] - y) < 0.03]
        return float(np.abs(band[:, 0]).max()) if len(band) else 0.15


@lru_cache(maxsize=2)
def _cached_bake(mesh, texture, h, w):
    return _bake(mesh, texture, h, w)


def bake(mesh: Mesh, texture: str, h: int, w: int, priority: np.ndarray | None = None) -> dict | None:
    # Layouts are immutable inputs to every finish. Share the expensive rasterization.
    return _cached_bake(mesh, texture, h, w) if priority is None else _bake(mesh, texture, h, w, priority)


def _bake(mesh: Mesh, texture: str, h: int, w: int, priority: np.ndarray | None = None) -> dict | None:
    """Every texel of a texture traced back to the bind pose: position, normal, body part and the index of the
    texture's triangle (in mesh.tris[texture] order) covering it (h x w), with a mask of the texels a triangle covers
    (seams padded a few texels outward). UVs outside 0..1 wrap, as the textures repeat. Where triangles share texels,
    the one with the highest priority (one per triangle, in the same order) keeps them, else the last one. None when
    the mesh does not use that texture."""
    tris = mesh.tris.get(texture)
    if not tris:
        return None
    tris = np.concatenate(tris)
    P = np.zeros((h, w, 3), np.float32)
    N = np.zeros((h, w, 3), np.float32)
    G = np.zeros((h, w, _GROUPS), np.float32)
    T = np.full((h, w), -1, np.int32)
    best = np.full((h, w), -np.inf, np.float32)
    cover = np.zeros((h, w), bool)
    uv = mesh.uv * np.array([w, h], np.float32)
    for k, (a, b, c) in enumerate(tris):
        pa, pb, pc = uv[a], uv[b], uv[c]
        x0 = int(np.floor(min(pa[0], pb[0], pc[0])))
        x1 = min(int(np.ceil(max(pa[0], pb[0], pc[0]))), x0 + w - 1)
        y0 = int(np.floor(min(pa[1], pb[1], pc[1])))
        y1 = min(int(np.ceil(max(pa[1], pb[1], pc[1]))), y0 + h - 1)
        area = (pb[0] - pa[0]) * (pc[1] - pa[1]) - (pb[1] - pa[1]) * (pc[0] - pa[0])
        if abs(area) < 1e-9:
            continue
        X, Y = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        wa = ((pb[0] - X) * (pc[1] - Y) - (pb[1] - Y) * (pc[0] - X)) / area
        wb = ((pc[0] - X) * (pa[1] - Y) - (pc[1] - Y) * (pa[0] - X)) / area
        wc = 1.0 - wa - wb
        inside = (wa >= -1e-4) & (wb >= -1e-4) & (wc >= -1e-4)
        if not inside.any():
            continue
        iy, ix = np.nonzero(inside)
        iy, ix = (iy + y0) % h, (ix + x0) % w
        ka, kb, kc = wa[inside][:, None], wb[inside][:, None], wc[inside][:, None]
        if priority is not None:
            keep = priority[k] >= best[iy, ix]
            iy, ix, ka, kb, kc = iy[keep], ix[keep], ka[keep], kb[keep], kc[keep]
            best[iy, ix] = priority[k]
        P[iy, ix] = ka * mesh.pos[a] + kb * mesh.pos[b] + kc * mesh.pos[c]
        N[iy, ix] = ka * mesh.nrm[a] + kb * mesh.nrm[b] + kc * mesh.nrm[c]
        G[iy, ix] = ka * mesh.groups[a] + kb * mesh.groups[b] + kc * mesh.groups[c]
        T[iy, ix] = k
        cover[iy, ix] = True
    # Pad the islands outward, so filtering at a seam never reaches an unpainted texel.
    for _ in range(max(3, max(h, w) // 256)):
        grow = ~cover
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            src = np.roll(cover, (dy, dx), (0, 1)) & grow
            if src.any():
                iy, ix = np.nonzero(src)
                sy, sx = (iy - dy) % h, (ix - dx) % w
                P[iy, ix], N[iy, ix], G[iy, ix], T[iy, ix] = P[sy, sx], N[sy, sx], G[sy, sx], T[sy, sx]
                cover[iy, ix] = True
                grow[iy, ix] = False
    N /= np.maximum(np.linalg.norm(N, axis=2, keepdims=True), 1e-6)
    return {"P": P, "N": N, "group": G.argmax(2), "tri": T, "cover": cover}


# ---------------------------------------------------------------------------------------------------------------------
# Shapes (signed distances in metres, negative inside)

def _seg(x, y, a, b):
    ax, ay = a
    bx, by = b
    pax, pay, bax, bay = x - ax, y - ay, bx - ax, by - ay
    t = np.clip((pax * bax + pay * bay) / max(bax * bax + bay * bay, 1e-12), 0.0, 1.0)
    return np.hypot(pax - bax * t, pay - bay * t)


def _circle(x, y, c, r):
    return np.hypot(x - c[0], y - c[1]) - r


def _ellipse(x, y, c, rx, ry):
    return (np.hypot((x - c[0]) / rx, (y - c[1]) / ry) - 1.0) * min(rx, ry)


def _poly(x, y, pts):
    """A convex polygon, either winding."""
    area = sum(pts[i][0] * pts[(i + 1) % len(pts)][1] - pts[(i + 1) % len(pts)][0] * pts[i][1] for i in range(len(pts)))
    s = -1.0 if area > 0 else 1.0
    d = None
    for i in range(len(pts)):
        (ax, ay), (bx, by) = pts[i], pts[(i + 1) % len(pts)]
        ex, ey = bx - ax, by - ay
        e = s * ((x - ax) * ey - (y - ay) * ex) / max(np.hypot(ex, ey), 1e-9)
        d = e if d is None else np.maximum(d, e)
    return d


def _long_bone(x, y, a, b, r, knob):
    """A long bone: a shaft of half width r and knobbly ends (two condyles each side, across the shaft)."""
    a, b = np.asarray(a, np.float32), np.asarray(b, np.float32)
    d = _seg(x, y, a, b) - r
    axis = b - a
    ln = float(np.hypot(*axis)) or 1.0
    perp = np.array([-axis[1], axis[0]]) / ln
    along = axis / ln
    for end, inward in ((a, along), (b, -along)):
        for s in (-1, 1):
            c = end + inward * knob * 0.6 + perp * s * knob * 0.55
            d = np.minimum(d, _circle(x, y, c, knob * 0.7))
    return d


def _polyline(x, y, pts, r0, r1):
    """A tapering curve through pts (half width r0 at the start to r1 at the end)."""
    d = None
    n = len(pts) - 1
    for i in range(n):
        r = r0 + (r1 - r0) * (i + 0.5) / n
        e = _seg(x, y, pts[i], pts[i + 1]) - r
        d = e if d is None else np.minimum(d, e)
    return d


def _union(*ds):
    out = None
    for d in ds:
        if d is not None:
            out = d if out is None else np.minimum(out, d)
    return out


class _Rig:
    """The joints the drawing needs (2D where it is drawn) and the body's proportions."""

    def __init__(self, mesh: Mesh):
        jn = mesh.joints
        self.m = mesh
        self.neck = jn["B Neck"]
        self.head = jn["B Head"]
        self.pelvis = jn["B Pelvis"]
        self.L = float(self.neck[1] - self.pelvis[1])  # neck base to hips
        self.height = float(self.head[1] - min(jn["B L Foot"][1], jn["B R Foot"][1]))
        self.shoulder = abs(float(jn["B L UpperArm"][0]))

    def side(self, s: str, part: str) -> np.ndarray:
        return self.m.joints[f"B {s} {part}"]


def _xy(p):
    return float(p[0]), float(p[1])


def _xz(p):
    return float(p[0]), float(p[2])


def _torso_front(rig: _Rig, x, y):
    yN, yP, L = float(rig.neck[1]), float(rig.pelvis[1]), rig.L
    sx = rig.shoulder
    parts = []
    # Sternum and its broad top.
    parts.append(_seg(x, y, (0, yN - 0.07 * L), (0, yN - 0.36 * L)) - 0.011)
    parts.append(_ellipse(x, y, (0, yN - 0.08 * L), 0.022, 0.03))
    # Ribs: out and down from the sternum, the lower ones from the costal arch, shorter.
    for i in range(10):
        y0 = yN - 0.09 * L - i * 0.042 * L
        hw = rig.m.half_width(y0) * 0.86
        x0 = 0.015 if i < 7 else 0.03 + (i - 6) * 0.022
        if hw <= x0 + 0.02:
            continue
        for s in (-1, 1):
            t = np.linspace(0, 1, 7)
            pts = [(s * (x0 + tt * (hw - x0)), y0 - 0.09 * L * tt ** 1.6 + 0.01 * np.sin(np.pi * tt)) for tt in t]
            parts.append(_polyline(x, y, pts, 0.0075, 0.006))
    # Clavicles.
    for s in (-1, 1):
        parts.append(_long_bone(x, y, (s * 0.022, yN - 0.03 * L), (s * 0.9 * sx, float(rig.side("L", "UpperArm")[1]) + 0.02),
                                0.0065, 0.009))
    # Lumbar spine below the ribs.
    parts.append(_spine(x, y, yN - 0.5 * L, yP + 0.14 * L, L))
    parts.append(_pelvis(rig, x, y, front=True))
    return _union(*parts)


def _spine(x, y, top, bottom, L):
    """Vertebrae from top down to bottom: rounded blocks with short side wings."""
    step = 0.047 * L
    d = None
    yy = top
    while yy > bottom:
        c = yy - step * 0.45
        block = np.hypot(np.maximum(np.abs(x) - 0.011, 0), np.maximum(np.abs(y - c) - step * 0.3, 0)) - 0.004
        wing = np.hypot(np.maximum(np.abs(x) - 0.024, 0), np.maximum(np.abs(y - c + step * 0.05) - step * 0.09, 0)) - 0.002
        e = np.minimum(block, wing)
        d = e if d is None else np.minimum(d, e)
        yy -= step
    return d


def _pelvis(rig: _Rig, x, y, front: bool):
    yP, L = float(rig.pelvis[1]), rig.L
    hw = rig.m.half_width(yP + 0.07 * L)
    parts = []
    for s in (-1, 1):
        wing = _ellipse(x, y, (s * 0.5 * hw, yP + 0.1 * L), 0.36 * hw, 0.11 * L)
        parts.append(wing)
        if front:
            # The ring of the obturator hole and the pubic bar to the middle.
            c = (s * 0.3 * hw, yP - 0.05 * L)
            ring = np.abs(_ellipse(x, y, c, 0.13 * hw, 0.055 * L)) - 0.007
            parts.append(ring)
            parts.append(_seg(x, y, (s * 0.15 * hw, yP - 0.1 * L), (0, yP - 0.08 * L)) - 0.012)
    if not front:
        parts.append(_poly(x, y, [(-0.22 * hw, yP + 0.12 * L), (0.22 * hw, yP + 0.12 * L), (0, yP - 0.12 * L)]))
    d = _union(*parts)
    # The hollow inside each wing.
    for s in (-1, 1):
        d = np.maximum(d, -_ellipse(x, y, (s * 0.55 * hw, yP + 0.13 * L), 0.1 * hw, 0.03 * L))
    return d


def _torso_back(rig: _Rig, x, y):
    yN, yP, L = float(rig.neck[1]), float(rig.pelvis[1]), rig.L
    sx = rig.shoulder
    parts = [_spine(x, y, yN + 0.02 * L, yP + 0.12 * L, L)]
    for i in range(10):
        y0 = yN - 0.07 * L - i * 0.042 * L
        hw = rig.m.half_width(y0) * 0.88
        if hw <= 0.05:
            continue
        for s in (-1, 1):
            t = np.linspace(0, 1, 7)
            pts = [(s * (0.03 + tt * (hw - 0.03)), y0 + 0.012 * np.sin(np.pi * tt) - 0.05 * L * tt ** 2) for tt in t]
            parts.append(_polyline(x, y, pts, 0.0075, 0.006))
    for s in (-1, 1):
        # Shoulder blades over the ribs, with their ridge.
        blade = _poly(x, y, [(s * 0.28 * sx, yN - 0.07 * L), (s * 0.88 * sx, yN - 0.06 * L), (s * 0.4 * sx, yN - 0.36 * L)])
        parts.append(blade - 0.006)
    parts.append(_pelvis(rig, x, y, front=False))
    return _union(*parts)


def _arm(rig: _Rig, s: str, x, y):
    up, fore, hand = (_xy(rig.side(s, p)) for p in ("UpperArm", "ForeArm", "Hand"))
    d = _long_bone(x, y, up, fore, 0.012, 0.018)
    fx, fy = fore
    hx, hy = hand
    ln = float(np.hypot(hx - fx, hy - fy)) or 1.0
    px, py = -(hy - fy) / ln, (hx - fx) / ln
    for k, r in ((1, 0.0065), (-1, 0.0055)):
        o = 0.0095 * k
        d = np.minimum(d, _seg(x, y, (fx + px * o, fy + py * o), (hx + px * o, hy + py * o)) - r)
    d = np.minimum(d, _circle(x, y, fore, 0.017))
    return np.minimum(d, _ellipse(x, y, hand, 0.02, 0.016))


def _leg(rig: _Rig, s: str, x, y, front: bool):
    thigh, calf, foot = (_xy(rig.side(s, p)) for p in ("Thigh", "Calf", "Foot"))
    out = 1.0 if thigh[0] > 0 else -1.0
    d = _long_bone(x, y, thigh, calf, 0.016, 0.024)
    d = np.minimum(d, _circle(x, y, (thigh[0] - out * 0.012, thigh[1] + 0.01), 0.025))  # the femur's head
    d = np.minimum(d, _long_bone(x, y, (calf[0] - out * 0.004, calf[1]), (foot[0] - out * 0.004, foot[1]), 0.013, 0.02))
    d = np.minimum(d, _seg(x, y, (calf[0] + out * 0.022, calf[1] - 0.03), (foot[0] + out * 0.02, foot[1] + 0.03)) - 0.0065)
    if front:
        d = np.minimum(d, _ellipse(x, y, (calf[0], calf[1] + 0.012), 0.021, 0.026))  # kneecap
    return d


def _hand_top(rig: _Rig, s: str, x, z):
    hand = _xz(rig.side(s, "Hand"))
    d = _ellipse(x, z, (hand[0] + (0.02 if hand[0] > 0 else -0.02), hand[1]), 0.02, 0.022)  # wrist bones
    for f in ("0", "1", "2", "3"):
        try:
            base, mid = _xz(rig.side(s, f"Finger{f}")), _xz(rig.side(s, f"Finger{f}1"))
        except KeyError:
            continue
        tip = (mid[0] + (mid[0] - base[0]) * 0.9, mid[1] + (mid[1] - base[1]) * 0.9)
        d = np.minimum(d, _seg(x, z, hand, base) - 0.0045)
        d = np.minimum(d, _seg(x, z, base, mid) - 0.0042)
        d = np.minimum(d, _seg(x, z, mid, tip) - 0.0036)
        for k in (base, mid):
            d = np.minimum(d, _circle(x, z, k, 0.0062))
    return d


def _foot_top(rig: _Rig, s: str, x, z):
    foot, toe = _xz(rig.side(s, "Foot")), _xz(rig.side(s, "Toe0"))
    d = _ellipse(x, z, (foot[0], foot[1] - 0.01), 0.022, 0.03)  # heel and ankle bones
    mid = (foot[0] + 0.45 * (toe[0] - foot[0]), foot[1] + 0.45 * (toe[1] - foot[1]))
    for k in range(5):
        o = (k - 2) * 0.011
        a = (mid[0] + o * 0.6, mid[1])
        b = (toe[0] + o, toe[1])
        c = (toe[0] + o * 1.15, toe[1] + 0.04 - abs(k - 1) * 0.004)
        d = np.minimum(d, _seg(x, z, a, b) - 0.0045)
        d = np.minimum(d, _seg(x, z, b, c) - 0.004)
        d = np.minimum(d, _circle(x, z, b, 0.0058))
    return np.minimum(d, _ellipse(x, z, (mid[0], mid[1] - 0.015), 0.024, 0.02))


def _skull_holes(rig: _Rig, x, y):
    """The face's dark parts (negative inside): eye sockets, nose, the gaps between the teeth and the cheek hollows."""
    hy = float(rig.head[1])
    Hs = 0.15 * rig.height
    holes = []
    for s in (-1, 1):
        holes.append(_ellipse(x, y, (s * 0.16 * Hs, hy + 0.47 * Hs), 0.115 * Hs, 0.1 * Hs))
        holes.append(_ellipse(x, y, (s * 0.25 * Hs, hy + 0.2 * Hs), 0.06 * Hs, 0.09 * Hs))
    holes.append(_poly(x, y, [(0, hy + 0.36 * Hs), (0.055 * Hs, hy + 0.25 * Hs), (-0.055 * Hs, hy + 0.25 * Hs)]))
    # The teeth: two rows split by a dark line, with dark gaps between them.
    band = (np.abs(x) < 0.17 * Hs) & (y > hy + 0.06 * Hs) & (y < hy + 0.19 * Hs)
    gap = np.minimum(np.abs(((x / (0.045 * Hs)) + 0.5) % 1.0 - 0.5) * 0.045 * Hs, np.abs(y - (hy + 0.125 * Hs)))
    teeth = np.where(band, gap - 0.0018, 1.0)
    # Lines either side of the teeth closing the mouth.
    holes.append(np.where(band, 1.0, _seg(x, y, (-0.17 * Hs, hy + 0.125 * Hs), (0.17 * Hs, hy + 0.125 * Hs)) - 0.002))
    holes.append(teeth)
    return _union(*holes)


def draw(layout: dict | None, colours: list[int], h: int, w: int, mesh: Mesh) -> np.ndarray:
    """The skeleton (h x w x 3, 0..1): colours are the suit, the bone, the bone's shade and the holes. layout is a
    texture's bake; None draws the flat front view for the swatch."""
    from .cosmetics import _rgb  # noqa: PLC0415

    suit, bone, shade, hole = (_rgb(c) for c in colours)
    rig = _Rig(mesh)
    if layout is None:
        # The swatch: the whole skeleton from the front, centred.
        top, bottom = rig.m.head_top + 0.03, float(min(rig.side("L", "Toe0")[1], rig.side("R", "Toe0")[1])) - 0.03
        reach = max(abs(float(rig.side(k, "Finger11")[0])) for k in ("L", "R")) + 0.04
        span = max(top - bottom, 2 * reach)
        mid = (top + bottom) / 2
        xs = (np.arange(w) + 0.5) / w * span - span / 2
        ys = mid + span / 2 - (np.arange(h) + 0.5) / h * span
        X, Y = np.meshgrid(xs.astype(np.float32), ys.astype(np.float32))
        d = _union(_torso_front(rig, X, Y), _arm(rig, "L", X, Y), _arm(rig, "R", X, Y), _leg(rig, "L", X, Y, True),
                   _leg(rig, "R", X, Y, True))
        hy, Hs = float(rig.head[1]), 0.15 * rig.height
        skull = _union(_ellipse(X, Y, (0, hy + 0.55 * Hs), 0.36 * Hs, 0.48 * Hs),
                       _ellipse(X, Y, (0, hy + 0.15 * Hs), 0.24 * Hs, 0.17 * Hs))
        d = np.minimum(d, np.maximum(skull, -_skull_holes(rig, X, Y)))
        aa = span / h
        cov = np.clip(0.5 - d / aa, 0, 1)
        inner = np.clip(-d / 0.008, 0, 1)
        col = shade + (bone - shade) * (0.35 + 0.65 * inner)[..., None]
        return suit + (col - suit) * cov[..., None]

    P, N, group, cover = layout["P"], layout["N"], layout["group"], layout["cover"]
    out = np.broadcast_to(suit, (h, w, 3)).copy()
    d_all = np.full((h, w), 1.0, np.float32)
    vis_all = np.zeros((h, w), np.float32)
    holes_all = np.full((h, w), 1.0, np.float32)

    def sel(g):
        return np.nonzero(cover & (group == g))

    def ss(e0, e1, v):
        t = np.clip((v - e0) / (e1 - e0), 0, 1)
        return t * t * (3 - 2 * t)

    # Head: bone all over, the face on the front.
    iy, ix = sel(HEAD)
    if len(iy):
        x, y, nz = P[iy, ix, 0], P[iy, ix, 1], N[iy, ix, 2]
        d_all[iy, ix] = -1.0
        vis_all[iy, ix] = 1.0
        holes_all[iy, ix] = np.where(nz > 0.25, _skull_holes(rig, x, y), 1.0)
    # Torso, arms and legs: front and back views.
    for g, fn in ((TORSO, None), (ARM_L, "L"), (ARM_R, "R"), (LEG_L, "L"), (LEG_R, "R")):
        iy, ix = sel(g)
        if not len(iy):
            continue
        x, y, nz = P[iy, ix, 0], P[iy, ix, 1], N[iy, ix, 2]
        front = nz >= 0
        if g == TORSO:
            d = np.where(front, _torso_front(rig, x, y), _torso_back(rig, x, y))
        elif g in (ARM_L, ARM_R):
            d = _arm(rig, fn, x, y)
        else:
            d = np.where(front, _leg(rig, fn, x, y, True), _leg(rig, fn, x, y, False))
        d_all[iy, ix] = d
        vis_all[iy, ix] = ss(0.15, 0.45, np.abs(nz))
    # Hands and feet: from above (and the palms and soles from below).
    for g, s, fn in ((HAND_L, "L", _hand_top), (HAND_R, "R", _hand_top), (FOOT_L, "L", _foot_top), (FOOT_R, "R", _foot_top)):
        iy, ix = sel(g)
        if not len(iy):
            continue
        x, z, ny = P[iy, ix, 0], P[iy, ix, 2], N[iy, ix, 1]
        d_all[iy, ix] = fn(rig, s, x, z)
        vis_all[iy, ix] = ss(0.15, 0.45, np.abs(ny)) * (1.0 if fn is _hand_top else (ny > 0))

    cov = np.clip(0.5 - d_all / AA, 0, 1) * vis_all
    inner = np.clip(-d_all / 0.007, 0, 1)
    col = shade + (bone - shade) * (0.4 + 0.6 * inner)[..., None]
    out = out + (col - out) * cov[..., None]
    holes = np.clip(0.5 - holes_all / AA, 0, 1)
    rim = np.clip(1.0 - np.abs(holes_all) / 0.004, 0, 1) * (holes_all > 0)
    out = out + (shade - out) * (0.5 * rim)[..., None]
    out = out + (hole - out) * holes[..., None]
    return out
