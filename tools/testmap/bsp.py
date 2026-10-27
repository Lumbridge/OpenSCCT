"""The world of a box-and-wedge map as a solid-leaf BSP, the form the converted maps' world collision has.

The empty space is the union of the carved spaces (the hall, the pool) less the union of the added solids. Its
boundary is cut out of the faces of every carve and solid (`boundary`), each polygon facing the empty side. A BSP is
then built over those polygons with every node a polygon's plane (`build`): a line walking the tree is in empty space
past the front of the last node it crossed and in solid past its back, which is how the engine walks the world BSP
(UModel::LineCheck: a CSG node makes its front empty and its back solid; spec/svmap/1.1/collision.md BSPN). Zones
come first, as a chain of planes that are not CSG nodes (no polygon, NotCsg), so every leaf lies in one zone.

Plain Python on floats: the result is the same on every run.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

EPS = 0.01                 # plane side tolerance (uu); the layout is on whole units
NOT_CSG = 0x01             # BSPN node flag: a plane with no polygon (zone split)


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def _dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


@dataclass(frozen=True)
class Plane:
    n: tuple[float, float, float]
    d: float

    def dist(self, p) -> float:
        return _dot(self.n, p) - self.d

    def flipped(self) -> "Plane":
        return Plane((-self.n[0], -self.n[1], -self.n[2]), -self.d)

    def same(self, other: "Plane") -> bool:
        return _dot(self.n, other.n) > 1 - 1e-6 and abs(self.d - other.d) < EPS


def plane_of(verts) -> Plane:
    n = _cross(_sub(verts[1], verts[0]), _sub(verts[2], verts[0]))
    length = math.sqrt(_dot(n, n))
    n = (n[0] / length, n[1] / length, n[2] / length)
    # Axis-aligned normals exactly: the planes of a box stay exact.
    n = tuple(0.0 if abs(c) < 1e-9 else (1.0 if c > 1 - 1e-9 else (-1.0 if c < -1 + 1e-9 else c)) for c in n)
    return Plane(n, _dot(n, verts[0]))


@dataclass
class Poly:
    verts: list
    plane: Plane
    material: str
    owner: int = 0            # the solid or carve it came from (priority among coplanar duplicates)

    def centroid(self):
        k = len(self.verts)
        return tuple(sum(v[i] for v in self.verts) / k for i in range(3))

    def area(self) -> float:
        total = (0.0, 0.0, 0.0)
        v0 = self.verts[0]
        for i in range(1, len(self.verts) - 1):
            c = _cross(_sub(self.verts[i], v0), _sub(self.verts[i + 1], v0))
            total = (total[0] + c[0], total[1] + c[1], total[2] + c[2])
        return 0.5 * math.sqrt(_dot(total, total))


FRONT, BACK, ON, SPLIT = 0, 1, 2, 3


def classify(poly: Poly, plane: Plane) -> int:
    front = back = False
    for v in poly.verts:
        d = plane.dist(v)
        if d > EPS:
            front = True
        elif d < -EPS:
            back = True
    if front and back:
        return SPLIT
    if front:
        return FRONT
    if back:
        return BACK
    return ON


def split(poly: Poly, plane: Plane) -> tuple[Poly | None, Poly | None]:
    """The parts of a polygon in front of and behind a plane it crosses (Sutherland-Hodgman)."""
    f, b = [], []
    n = len(poly.verts)
    for i in range(n):
        a, c = poly.verts[i], poly.verts[(i + 1) % n]
        da, dc = plane.dist(a), plane.dist(c)
        if da >= -EPS:
            f.append(a)
        if da <= EPS:
            b.append(a)
        if (da > EPS and dc < -EPS) or (da < -EPS and dc > EPS):
            t = da / (da - dc)
            p = (a[0] + (c[0] - a[0]) * t, a[1] + (c[1] - a[1]) * t, a[2] + (c[2] - a[2]) * t)
            p = tuple(float(round(x * 64.0) / 64.0) for x in p)     # keep split points on a 1/64 grid
            f.append(p)
            b.append(p)

    def make(vs):
        clean = []
        for v in vs:
            if not clean or max(abs(v[i] - clean[-1][i]) for i in range(3)) > 1e-6:
                clean.append(v)
        if len(clean) > 1 and max(abs(clean[0][i] - clean[-1][i]) for i in range(3)) <= 1e-6:
            clean.pop()
        if len(clean) < 3:
            return None
        q = Poly(clean, poly.plane, poly.material, poly.owner)
        return q if q.area() > 1e-3 else None

    return make(f), make(b)


@dataclass
class Convex:
    """A convex volume: its outward planes and its faces (outward polygons)."""
    faces: list[Poly]
    index: int

    @property
    def planes(self) -> list[Plane]:
        return [f.plane for f in self.faces]

    def bounds(self):
        vs = [v for f in self.faces for v in f.verts]
        return [min(v[i] for v in vs) for i in range(3)], [max(v[i] for v in vs) for i in range(3)]


def _subtract(polys: list[Poly], vol: Convex, keep_same: bool) -> list[Poly]:
    """Polygons less the parts inside the volume, or on its boundary facing away from where its face faces (two faces
    touching back to back). A part on a face of the volume that faces the same way is kept when `keep_same`."""
    out = []
    vlo, vhi = vol.bounds()
    for p in polys:
        plo = [min(v[i] for v in p.verts) for i in range(3)]
        phi = [max(v[i] for v in p.verts) for i in range(3)]
        if any(plo[i] > vhi[i] + EPS or phi[i] < vlo[i] - EPS for i in range(3)):
            out.append(p)                                 # nowhere near the volume
            continue
        inside = [p]
        for plane in vol.planes:
            nxt = []
            for q in inside:
                c = classify(q, plane)
                if c == FRONT:
                    out.append(q)
                elif c == SPLIT:
                    fr, bk = split(q, plane)
                    if fr:
                        out.append(fr)
                    if bk:
                        nxt.append(bk)
                else:
                    nxt.append(q)
            inside = nxt
            if not inside:
                break
        for q in inside:
            on = [plane for plane in vol.planes if classify(q, plane) == ON]
            if not on:
                continue                                  # strictly inside: dropped
            facing = on[0]
            if _dot(q.plane.n, facing.n) > 0.5 and keep_same:
                out.append(q)                             # a coplanar duplicate this polygon wins
    return out


def boundary(carves: list[Convex], solids: list[Convex], cut_carve) -> list[Poly]:
    """The polygons between empty space (the carves less the solids) and solid, each facing the empty side.
    `cut_carve(poly, carve_index)` removes the parts of a carve's face that open into another carve."""
    out: list[Poly] = []
    for c in carves:
        for f in c.faces:
            inward = Poly(list(reversed(f.verts)), f.plane.flipped(), f.material, c.index)
            parts = cut_carve(inward, c.index)
            for s in solids:
                parts = _subtract(parts, s, keep_same=False)
            out += parts
    for s in solids:
        for f in s.faces:
            parts = [Poly(list(f.verts), f.plane, f.material, s.index)]
            for o in solids:
                if o.index != s.index:
                    # Coplanar faces of two solids facing the same way: the later brush's face is the one the
                    # editor's CSG keeps (its texture shows), so this one stays only against earlier solids.
                    parts = _subtract(parts, o, keep_same=o.index < s.index)
            # Parts outside every carve (against the hall's walls, facing out) are in solid.
            kept = []
            for q in parts:
                if any(_inside_or_facing_in(q, c) for c in carves):
                    kept.append(q)
            out += kept
    return out


def _inside_or_facing_in(q: Poly, c: Convex) -> bool:
    centre = q.centroid()
    probe = (centre[0] + q.plane.n[0] * 0.5, centre[1] + q.plane.n[1] * 0.5, centre[2] + q.plane.n[2] * 0.5)
    return all(plane.dist(probe) < 0 for plane in c.planes)


@dataclass
class Node:
    plane: Plane
    polys: list[Poly] = field(default_factory=list)
    front: object = None         # Node or Leaf
    back: object = None
    csg: bool = True


@dataclass
class Leaf:
    empty: bool
    zone: int


def _pick(polys: list[Poly]) -> Poly:
    """The splitter: the polygon whose plane splits fewest others, larger first on ties (deterministic)."""
    best, best_key = None, None
    sample = polys if len(polys) <= 64 else polys[:: max(1, len(polys) // 64)]
    for p in sample:
        splits = sum(1 for q in polys if classify(q, p.plane) == SPLIT)
        key = (splits, -round(p.area(), 3))
        if best_key is None or key < best_key:
            best, best_key = p, key
    return best


def _build(polys: list[Poly], zone: int, empty_default: bool):
    if not polys:
        return Leaf(empty_default, zone if empty_default else 0)
    sp = _pick(polys)
    node = Node(sp.plane)
    fr, bk = [], []
    for q in polys:
        c = classify(q, sp.plane)
        if c == ON:
            if _dot(q.plane.n, sp.plane.n) > 0:
                node.polys.append(q)
            else:
                bk.append(q)
        elif c == FRONT:
            fr.append(q)
        elif c == BACK:
            bk.append(q)
        else:
            a, b = split(q, sp.plane)
            if a:
                fr.append(a)
            if b:
                bk.append(b)
    node.front = _build(fr, zone, True)
    node.back = _build(bk, zone, False)
    return node


def build(polys: list[Poly], zones: list[tuple[int, tuple, tuple]], outside_zone: int):
    """The tree: `zones` [(zone index, lo, hi)] are boxes split off first by chains of non-CSG planes (outward
    normals: front is outside the box); what is outside them all is `outside_zone`."""

    def chain(polys, rest):
        if not rest:
            return _build(polys, outside_zone, False)
        zi, lo, hi = rest[0]
        planes = [Plane((-1.0, 0.0, 0.0), -lo[0]), Plane((1.0, 0.0, 0.0), hi[0]), Plane((0.0, -1.0, 0.0), -lo[1]),
                  Plane((0.0, 1.0, 0.0), hi[1]), Plane((0.0, 0.0, -1.0), -lo[2]), Plane((0.0, 0.0, 1.0), hi[2])]
        top = None
        parent = None
        inside = polys
        for plane in planes:
            fr, bk = [], []
            for q in inside:
                c = classify(q, plane)
                if c == ON:
                    (fr if _dot(q.plane.n, plane.n) > 0 else bk).append(q)
                elif c == FRONT:
                    fr.append(q)
                elif c == BACK:
                    bk.append(q)
                else:
                    a, b = split(q, plane)
                    if a:
                        fr.append(a)
                    if b:
                        bk.append(b)
            node = Node(plane, csg=False)
            node.front = chain(fr, rest[1:])
            if parent is None:
                top = node
            else:
                parent.back = node
            parent = node
            inside = bk
        parent.back = _build(inside, zi, False)
        return top

    return chain(polys, list(zones))


def flatten(root):
    """Nodes in depth-first order (front before back) as records: (plane, front, back, polys, csg, zone_front,
    zone_back); children are node indices or -1 for a leaf."""
    nodes = []

    def zone_of(child):
        return child.zone if isinstance(child, Leaf) else 0

    def visit(n):
        i = len(nodes)
        nodes.append(None)
        f = -1 if isinstance(n.front, Leaf) else visit(n.front)
        b = -1 if isinstance(n.back, Leaf) else visit(n.back)
        nodes[i] = (n.plane, f, b, n.polys, n.csg, zone_of(n.front), zone_of(n.back))
        return i

    if isinstance(root, Leaf):
        return nodes
    visit(root)
    return nodes


def classify_point(root, p) -> bool:
    """True when p is in empty space (for tests): the leaf's side."""
    n = root
    while isinstance(n, Node):
        n = n.front if n.plane.dist(p) >= 0 else n.back
    return n.empty
