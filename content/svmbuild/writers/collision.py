"""sim/collision.bin writer and reader (spec/svmap/1.0/collision.md, 1.1 additions in spec/svmap/1.1/collision.md).

1.1 adds three optional chunks after ZONE, each parallel to a 1.0 chunk (record i extends record i there):
CSRC (colliders: source object, asset, collision set, mesh variants, trace use), TFLX (collision materials: original
poly flags and material bits) and CYLX (cylinders: kind and interaction class). A file without them is a 1.0 file and
keeps minor version 0; 1.0 readers skip them as unknown chunks.
"""

from __future__ import annotations

import math
import struct
from dataclasses import dataclass, field

MAGIC = b"SVCL"
MAJOR = 1
MINOR = 0
NONE = 0xFFFFFFFF
CHUNK_ORDER = (b"STRS", b"VERT", b"TRIS", b"TFLG", b"PLNS", b"HULL", b"BSPN", b"COLL", b"CYLS", b"ZONE")
EXT_ORDER = (b"CSRC", b"TFLX", b"CYLX")          # 1.1, optional, after ZONE, in this order
CHUNK_VERSION = {c: 1 for c in CHUNK_ORDER + EXT_ORDER}

FILE_HEADER = struct.Struct("<4sHHII")
CHUNK_HEADER = struct.Struct("<4sIII")
REC = {
    b"VERT": struct.Struct("<3f"),
    b"TRIS": struct.Struct("<IIIHH"),
    b"TFLG": struct.Struct("<BBHI"),
    b"PLNS": struct.Struct("<4f"),
    b"HULL": struct.Struct("<IIIIHHI3f3f"),
    b"BSPN": struct.Struct("<4fiiiiIIHBBI"),
    b"COLL": struct.Struct("<BBHIII8I3f3i3f3f3f3fII"),
    b"CYLS": struct.Struct("<IHH3fffI"),
    b"ZONE": struct.Struct("<II3f3f"),
    b"CSRC": struct.Struct("<IIBBHHH"),
    b"TFLX": struct.Struct("<IHBB"),
    b"CYLX": struct.Struct("<BBHI"),
}
STRIDES = {c: s.size for c, s in REC.items()}
assert STRIDES == {b"VERT": 12, b"TRIS": 16, b"TFLG": 8, b"PLNS": 16, b"HULL": 48, b"BSPN": 48, b"COLL": 128,
                   b"CYLS": 32, b"ZONE": 32, b"CSRC": 16, b"TFLX": 8, b"CYLX": 8}

KINDS = ("worldBsp", "brush", "meshPoly", "meshHull", "mover", "glass", "gpo", "attached")
COLLIDER_FLAGS = ("collideActors", "blockActors", "blockPlayers", "blockZeroExtent", "blockNonZeroExtent",
                  "blockCamera", "bulletGoesThrough", "startsDisabled")
MATERIAL_FLAGS = ("climbable", "noSplitJump", "noStickyCam", "semisolid", "invisible")
ZONE_FLAGS = ("skyZone",)
# CSRC.set: which geometry of the source a collider holds.
SOURCE_SETS = ("worldBsp", "brushModel", "perPoly", "simple")
# CSRC.traceUse bits: the traces this collider answers when its source has more than one set.
TRACE_USE = ("zeroExtent", "nonZeroExtent")
# TFLX.materialBits.
MATERIAL_BITS = ("hideInEMFVision", "hideInThermalVision", "echelonLightThrough")
# CYLX.kind.
CYLINDER_KINDS = ("collision", "interaction")


def flag_bits(names, table) -> int:
    bits = 0
    for n in names:
        bits |= 1 << table.index(n)
    return bits


def flag_names(bits: int, table) -> list[str]:
    return [n for i, n in enumerate(table) if bits & (1 << i)]


class CollisionError(ValueError):
    pass


Vec3 = tuple[float, float, float]


@dataclass
class Material:
    surface_type: int = 0
    flags: int = 0
    name: int = NONE


@dataclass
class Hull:
    plane_start: int
    plane_count: int
    vert_start: int
    vert_count: int
    material: int
    min: Vec3
    max: Vec3


@dataclass
class BspNode:
    plane: tuple[float, float, float, float]
    front: int = -1
    back: int = -1
    coplanar: int = -1
    collision_bound: int = -1
    tri_start: int = 0
    tri_count: int = 0
    node_flags: int = 0
    zone_front: int = 0
    zone_back: int = 0


@dataclass
class Collider:
    kind: int
    flags: int
    actor: int = NONE
    parent: int = NONE
    vert_start: int = 0
    vert_count: int = 0
    tri_start: int = 0
    tri_count: int = 0
    bsp_start: int = 0
    bsp_count: int = 0
    hull_start: int = 0
    hull_count: int = 0
    location: Vec3 = (0.0, 0.0, 0.0)
    rotation: tuple[int, int, int] = (0, 0, 0)
    scale: Vec3 = (1.0, 1.0, 1.0)
    pre_pivot: Vec3 = (0.0, 0.0, 0.0)
    aabb_min: Vec3 = (0.0, 0.0, 0.0)
    aabb_max: Vec3 = (0.0, 0.0, 0.0)


@dataclass
class Cylinder:
    actor: int
    flags: int
    location: Vec3
    radius: float
    height: float


@dataclass
class Zone:
    actor: int
    flags: int
    aabb_min: Vec3
    aabb_max: Vec3


@dataclass
class ColliderSource:
    """CSRC record: where collider i came from."""
    source: int = NONE           # STRS: export name of the source actor (also for actors not in gameplay.json)
    asset: int = NONE            # STRS: static mesh or model asset path
    set: int = 0                 # SOURCE_SETS index
    variants: int = 1            # bit 0 initial mesh, bit 1 TriggeredMesh, bit 2 + i DamagedMeshes[i]
    trace_use: int = 3           # TRACE_USE bits


@dataclass
class MaterialExt:
    """TFLX record: more about collision material i."""
    poly_flags: int = 0          # original UE2 PolyFlags of the BSP surface (0 for meshes)
    material_bits: int = 0       # MATERIAL_BITS
    occlusion: int = 0           # original EOcclusionType


@dataclass
class CylinderExt:
    """CYLX record: more about cylinder i."""
    kind: int = 0                # CYLINDER_KINDS
    interaction_class: int = NONE  # STRS: class path of the interaction an interaction cylinder belongs to


@dataclass
class CollisionData:
    strings: list[str] = field(default_factory=list)
    verts: list[Vec3] = field(default_factory=list)
    tris: list[tuple[int, int, int, int, int]] = field(default_factory=list)
    materials: list[Material] = field(default_factory=list)
    planes: list[tuple[float, float, float, float]] = field(default_factory=list)
    hulls: list[Hull] = field(default_factory=list)
    bsp: list[BspNode] = field(default_factory=list)
    colliders: list[Collider] = field(default_factory=list)
    cylinders: list[Cylinder] = field(default_factory=list)
    zones: list[Zone] = field(default_factory=list)
    # 1.1 chunks (None: not written, a 1.0 file).
    sources: list[ColliderSource] | None = None
    material_ext: list[MaterialExt] | None = None
    cylinder_ext: list[CylinderExt] | None = None
    # Chunks a reader skipped (fourcc, version, count, payload).
    unknown: list[tuple[bytes, int, int, bytes]] = field(default_factory=list)
    _index: dict = field(default_factory=dict, repr=False)

    @property
    def extended(self) -> bool:
        return self.sources is not None or self.material_ext is not None or self.cylinder_ext is not None

    def string(self, s: str) -> int:
        """Index of s in the string table, added on first use."""
        if len(self._index) != len(self.strings):
            self._index = {v: i for i, v in enumerate(self.strings)}
        i = self._index.get(s)
        if i is None:
            self.strings.append(s)
            i = self._index[s] = len(self.strings) - 1
        return i

    def name_of(self, index: int) -> str | None:
        return None if index == NONE else self.strings[index]


def _align16(n: int) -> int:
    return (n + 15) & ~15


def _check_finite(values) -> None:
    for v in values:
        if isinstance(v, float) and not math.isfinite(v):
            raise CollisionError(f"non-finite float {v!r}")


def _payloads(d: CollisionData) -> dict[bytes, tuple[int, bytes]]:
    out: dict[bytes, tuple[int, bytes]] = {}
    blob = bytearray()
    offsets = []
    for s in d.strings:
        offsets.append(len(blob))
        blob += s.encode("utf-8") + b"\0"
    out[b"STRS"] = (len(d.strings), struct.pack(f"<{len(offsets)}I", *offsets) + bytes(blob))

    def pack(cc: bytes, rows) -> None:
        rec = REC[cc]
        parts = []
        for r in rows:
            _check_finite(r)
            parts.append(rec.pack(*r))
        out[cc] = (len(parts), b"".join(parts))

    pack(b"VERT", d.verts)
    pack(b"TRIS", d.tris)
    pack(b"TFLG", [(m.surface_type, 0, m.flags, m.name) for m in d.materials])
    pack(b"PLNS", d.planes)
    pack(b"HULL", [(h.plane_start, h.plane_count, h.vert_start, h.vert_count, h.material, 0, 0, *h.min, *h.max)
                   for h in d.hulls])
    pack(b"BSPN", [(*n.plane, n.front, n.back, n.coplanar, n.collision_bound, n.tri_start, n.tri_count,
                    n.node_flags, n.zone_front, n.zone_back, 0) for n in d.bsp])
    pack(b"COLL", [(c.kind, 0, c.flags, c.actor, c.parent, 0, c.vert_start, c.vert_count, c.tri_start, c.tri_count,
                    c.bsp_start, c.bsp_count, c.hull_start, c.hull_count, *c.location, *c.rotation, *c.scale,
                    *c.pre_pivot, *c.aabb_min, *c.aabb_max, 0, 0) for c in d.colliders])
    pack(b"CYLS", [(c.actor, c.flags, 0, *c.location, c.radius, c.height, 0) for c in d.cylinders])
    pack(b"ZONE", [(z.actor, z.flags, *z.aabb_min, *z.aabb_max) for z in d.zones])
    if d.extended:
        pack(b"CSRC", [(x.source, x.asset, x.set, 0, x.variants, x.trace_use, 0) for x in (d.sources or [])])
        pack(b"TFLX", [(x.poly_flags, x.material_bits, x.occlusion, 0) for x in (d.material_ext or [])])
        pack(b"CYLX", [(x.kind, 0, 0, x.interaction_class) for x in (d.cylinder_ext or [])])
    return out


def write_collision(d: CollisionData) -> bytes:
    payloads = _payloads(d)
    order = CHUNK_ORDER + (EXT_ORDER if d.extended else ())
    out = bytearray(FILE_HEADER.pack(MAGIC, MAJOR, 1 if d.extended else MINOR, len(order), 0))
    for cc in order:
        count, payload = payloads[cc]
        out += CHUNK_HEADER.pack(cc, CHUNK_VERSION[cc], len(payload), count)
        out += payload
        out += bytes(_align16(len(out)) - len(out))
    assert len(out) % 16 == 0
    return bytes(out)


def _read_strings(count: int, payload: bytes) -> list[str]:
    if len(payload) < 4 * count:
        raise CollisionError("STRS offsets truncated")
    offsets = struct.unpack_from(f"<{count}I", payload, 0)
    blob = payload[4 * count:]
    out = []
    for i, off in enumerate(offsets):
        end = blob.find(b"\0", off)
        if off > len(blob) or end < 0:
            raise CollisionError(f"STRS string {i} out of bounds")
        try:
            out.append(blob[off:end].decode("utf-8"))
        except UnicodeDecodeError as e:
            raise CollisionError(f"STRS string {i} is not UTF-8") from e
    return out


def read_collision(data: bytes, strict: bool = True) -> CollisionData:
    """Parse collision.bin. strict also enforces the 1.0 writer rules (order, padding, all chunks present)."""
    if len(data) < 16:
        raise CollisionError("file too small")
    magic, major, minor, chunk_count, flags = FILE_HEADER.unpack_from(data, 0)
    if magic != MAGIC:
        raise CollisionError(f"bad magic {magic!r}")
    if major != MAJOR:
        raise CollisionError(f"unsupported major version {major}")
    if strict and (flags != 0 or len(data) % 16):
        raise CollisionError("header flags must be 0 and the size a multiple of 16")
    pos = 16
    seen: dict[bytes, tuple[int, bytes]] = {}
    order: list[bytes] = []
    d = CollisionData()
    for _ in range(chunk_count):
        if pos % 16:
            raise CollisionError(f"chunk at {pos} is not 16-byte aligned")
        if pos + 16 > len(data):
            raise CollisionError("chunk header truncated")
        cc, version, size, count = CHUNK_HEADER.unpack_from(data, pos)
        start = pos + 16
        end = start + size
        if end > len(data):
            raise CollisionError(f"{cc!r} payload truncated")
        payload = data[start:end]
        nxt = _align16(end)
        if strict and any(data[end:nxt]):
            raise CollisionError(f"{cc!r} padding is not zero")
        pos = nxt
        if cc in CHUNK_VERSION:
            if cc in seen:
                raise CollisionError(f"duplicate chunk {cc!r}")
            if version != CHUNK_VERSION[cc]:
                raise CollisionError(f"{cc!r} version {version} is not supported")
            if cc != b"STRS" and size != count * STRIDES[cc]:
                raise CollisionError(f"{cc!r} size {size} does not match count {count}")
            seen[cc] = (count, payload)
            order.append(cc)
        else:
            d.unknown.append((cc, version, count, payload))
    if strict:
        base, ext = tuple(order[:len(CHUNK_ORDER)]), tuple(order[len(CHUNK_ORDER):])
        if base != CHUNK_ORDER or ext not in ((), EXT_ORDER):
            raise CollisionError(f"chunks {order} are not the 1.0 set in order (plus the 1.1 set)")
        if pos != len(data):
            raise CollisionError("trailing bytes after the last chunk")

    def rows(cc: bytes):
        if cc not in seen:
            return []
        count, payload = seen[cc]
        return [REC[cc].unpack_from(payload, i * STRIDES[cc]) for i in range(count)]

    if b"STRS" in seen:
        d.strings = _read_strings(*seen[b"STRS"])
    d.verts = [tuple(r) for r in rows(b"VERT")]
    d.tris = [tuple(r) for r in rows(b"TRIS")]
    d.materials = [Material(r[0], r[2], r[3]) for r in rows(b"TFLG")]
    d.planes = [tuple(r) for r in rows(b"PLNS")]
    d.hulls = [Hull(r[0], r[1], r[2], r[3], r[4], tuple(r[7:10]), tuple(r[10:13])) for r in rows(b"HULL")]
    d.bsp = [BspNode(tuple(r[0:4]), r[4], r[5], r[6], r[7], r[8], r[9], r[10], r[11], r[12]) for r in rows(b"BSPN")]
    d.colliders = [Collider(kind=r[0], flags=r[2], actor=r[3], parent=r[4], vert_start=r[6], vert_count=r[7],
                            tri_start=r[8], tri_count=r[9], bsp_start=r[10], bsp_count=r[11], hull_start=r[12],
                            hull_count=r[13], location=tuple(r[14:17]), rotation=tuple(r[17:20]),
                            scale=tuple(r[20:23]), pre_pivot=tuple(r[23:26]), aabb_min=tuple(r[26:29]),
                            aabb_max=tuple(r[29:32])) for r in rows(b"COLL")]
    d.cylinders = [Cylinder(r[0], r[1], tuple(r[3:6]), r[6], r[7]) for r in rows(b"CYLS")]
    d.zones = [Zone(r[0], r[1], tuple(r[2:5]), tuple(r[5:8])) for r in rows(b"ZONE")]
    if b"CSRC" in seen:
        d.sources = [ColliderSource(r[0], r[1], r[2], r[4], r[5]) for r in rows(b"CSRC")]
    if b"TFLX" in seen:
        d.material_ext = [MaterialExt(r[0], r[1], r[2]) for r in rows(b"TFLX")]
    if b"CYLX" in seen:
        d.cylinder_ext = [CylinderExt(r[0], r[3]) for r in rows(b"CYLX")]
    check_collision(d)
    return d


def check_collision(d: CollisionData) -> None:
    """Range and reference checks shared by the reader and the validator."""
    ns, nv, nt, nm, np_, nh, nb, nc = (len(d.strings), len(d.verts), len(d.tris), len(d.materials), len(d.planes),
                                       len(d.hulls), len(d.bsp), len(d.colliders))

    def rng(what: str, start: int, count: int, limit: int) -> None:
        if start + count > limit:
            raise CollisionError(f"{what} range {start}+{count} exceeds {limit}")

    def sref(what: str, i: int) -> None:
        if i != NONE and i >= ns:
            raise CollisionError(f"{what} string index {i} out of range")

    for v in d.verts:
        _check_finite(v)
    for p in d.planes:
        _check_finite(p)
    for i, (a, b, c, coll, mat) in enumerate(d.tris):
        if max(a, b, c) >= nv:
            raise CollisionError(f"triangle {i} vertex index out of range")
        if coll >= nc:
            raise CollisionError(f"triangle {i} collider {coll} out of range")
        col = d.colliders[coll]
        if not (col.tri_start <= i < col.tri_start + col.tri_count):
            raise CollisionError(f"triangle {i} is outside its collider {coll} triangle range")
        if mat >= nm:
            raise CollisionError(f"triangle {i} material {mat} out of range")
    for i, m in enumerate(d.materials):
        sref(f"material {i}", m.name)
        if m.flags >> len(MATERIAL_FLAGS):
            raise CollisionError(f"material {i} sets reserved flag bits")
    for i, h in enumerate(d.hulls):
        rng(f"hull {i} planes", h.plane_start, h.plane_count, np_)
        rng(f"hull {i} corners", h.vert_start, h.vert_count, nv)
        if h.material >= nm:
            raise CollisionError(f"hull {i} material out of range")
        _check_finite((*h.min, *h.max))
    for i, n in enumerate(d.bsp):
        for child in (n.front, n.back, n.coplanar):
            if child < -1 or child >= nb:
                raise CollisionError(f"bsp node {i} child {child} out of range")
        if n.collision_bound < -1 or n.collision_bound >= nh:
            raise CollisionError(f"bsp node {i} collision bound out of range")
        rng(f"bsp node {i} triangles", n.tri_start, n.tri_count, nt)
    for i, c in enumerate(d.colliders):
        if c.kind >= len(KINDS):
            raise CollisionError(f"collider {i} kind {c.kind} unknown")
        if c.flags >> len(COLLIDER_FLAGS):
            raise CollisionError(f"collider {i} sets reserved flag bits")
        sref(f"collider {i} actor", c.actor)
        sref(f"collider {i} parent", c.parent)
        rng(f"collider {i} vertices", c.vert_start, c.vert_count, nv)
        rng(f"collider {i} triangles", c.tri_start, c.tri_count, nt)
        rng(f"collider {i} bsp", c.bsp_start, c.bsp_count, nb)
        rng(f"collider {i} hulls", c.hull_start, c.hull_count, nh)
        _check_finite((*c.location, *c.scale, *c.pre_pivot, *c.aabb_min, *c.aabb_max))
        if any(lo > hi for lo, hi in zip(c.aabb_min, c.aabb_max)):
            raise CollisionError(f"collider {i} bounds are inverted")
    for i, c in enumerate(d.cylinders):
        sref(f"cylinder {i} actor", c.actor)
        if c.actor == NONE:
            raise CollisionError(f"cylinder {i} has no actor")
        _check_finite((*c.location, c.radius, c.height))
    for i, z in enumerate(d.zones):
        sref(f"zone {i} actor", z.actor)
        if z.flags >> len(ZONE_FLAGS):
            raise CollisionError(f"zone {i} sets reserved flag bits")
    for name, rows_, n in (("CSRC", d.sources, nc), ("TFLX", d.material_ext, nm),
                           ("CYLX", d.cylinder_ext, len(d.cylinders))):
        if rows_ is not None and len(rows_) != n:
            raise CollisionError(f"{name} has {len(rows_)} records for {n}")
    for i, x in enumerate(d.sources or []):
        sref(f"collider source {i}", x.source)
        sref(f"collider asset {i}", x.asset)
        if x.set >= len(SOURCE_SETS) or x.trace_use >> len(TRACE_USE):
            raise CollisionError(f"collider source {i} set or trace use out of range")
    for i, x in enumerate(d.material_ext or []):
        if x.material_bits >> len(MATERIAL_BITS):
            raise CollisionError(f"material {i} sets reserved TFLX bits")
    for i, x in enumerate(d.cylinder_ext or []):
        sref(f"cylinder {i} interaction class", x.interaction_class)
        if x.kind >= len(CYLINDER_KINDS):
            raise CollisionError(f"cylinder {i} kind {x.kind} unknown")
