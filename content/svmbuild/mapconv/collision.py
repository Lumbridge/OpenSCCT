"""sim/collision.bin of a converted map (spec/svmap/1.1/collision.md).

Colliders, in this order:

1. worldBsp: the level's Model. Every node polygon whose surface is solid becomes triangles (fans of the node's
   vertex pool, wound so the front face follows the node plane), including PF_Invisible and PF_Semisolid surfaces,
   which keep those flags in their TFLG material; PF_NotSolid and PF_Portal surfaces (zone portals, anti-portal
   sheets) collide with nothing and are left out. Every BSP node is kept in BSPN, in the original order, with its
   front, back and coplanar links, node flags and zones, for zone queries; a node's triangles are its polygon's.
   A node with an original collision bound (UModel LeafHulls: the convex solid region behind it) points at a HULL
   record of that region (its oriented node planes and bounding box); these records lie outside every collider's
   hull range, so the geometry rule does not see them. Vertices are the model's Points, so the original topology
   is kept.
2. brush: every gameplay actor with a Brush (BlockingVolume, Volume, SDamageVolume, PhysicsVolume ...), in brush
   local space with the actor's transform; its nodes go to BSPN and, when the brush is convex, its planes to HULL.
3. static meshes: every actor (gameplay or render) whose collision primitive is its static mesh: DrawType
   DT_StaticMesh, a StaticMesh, no Brush, bUseCylinderCollision false, and bCollideActors true. One collider per
   collision set the mesh has (per-polygon triangles and the simple-collision triangles from scctformats.smcollision),
   in mesh local space with the actor's transform, sharing vertices between actors of the same mesh. Kind: mover
   (Mover and subclasses), glass (SVitre), gpo (SBasicGamePlayObject and subclasses that are not bWorldGeometry),
   attached (Base is a mover or animated mesh), else meshPoly / meshHull. Gameplay objects also get colliders for their TriggeredMesh and DamagedMeshes
   stages (startsDisabled; CSRC.variants says which stages use the mesh).
4. Flags come from the actor's bCollideActors, bBlockActors, bBlockPlayers, bBlockZeroExtentTraces,
   bBlockNonZeroExtentTraces, bBlockCamera and bBulletGoTru, with the class defaults for what the map does not store.

Cylinders (CYLS): gameplay actors that collide as cylinders (triggers, presence triggers, pickups), and the
interaction cylinders of gameplay objects with an InteractionClass (CYLX kind interaction: radius fInteractionRadius,
else CollisionRadius + 150; height likewise, as SInteractObject.PostBeginPlay sets them; classes that resize them at
run time do so after).

Zones (ZONE): the model's zone table; zone 0 is the LevelInfo; bounds from the BSP polygons on either side.
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

import numpy as np

from ..sources import modeltail
from ..sources.env import import_formats
from ..sources.typedprops import Ref
from ..writers import collision as C
from .source import ActorInfo, MapSource

FLAG_PROPS = (("collideActors", "bCollideActors"), ("blockActors", "bBlockActors"), ("blockPlayers", "bBlockPlayers"),
              ("blockZeroExtent", "bBlockZeroExtentTraces"), ("blockNonZeroExtent", "bBlockNonZeroExtentTraces"),
              ("blockCamera", "bBlockCamera"), ("bulletGoesThrough", "bBulletGoTru"))
WORLD_FLAGS = C.flag_bits(["collideActors", "blockActors", "blockPlayers", "blockZeroExtent", "blockNonZeroExtent",
                           "blockCamera"], C.COLLIDER_FLAGS)
STARTS_DISABLED = C.flag_bits(["startsDisabled"], C.COLLIDER_FLAGS)
DT_STATICMESH = 8
KIND = {k: i for i, k in enumerate(C.KINDS)}
SET = {k: i for i, k in enumerate(C.SOURCE_SETS)}
DEGENERATE = 1e-6
CONVEX_EPS = 0.05
# Defaults of the intrinsic StaticMesh class (live class default object, docs/spikes/C2-staticmesh-collision.md).
STATICMESH_DEFAULTS = {"UseSimpleLineCollision": False, "UseSimpleBoxCollision": True}


def rotation_matrix(rot) -> np.ndarray:
    """UE2 FRotationMatrix rows (conventions.md), float64; for bounds only."""
    p, y, r = (float(v) * math.pi / 32768.0 for v in rot)
    sp, cp, sy, cy, sr, cr = math.sin(p), math.cos(p), math.sin(y), math.cos(y), math.sin(r), math.cos(r)
    return np.array([[cp * cy, cp * sy, sp],
                     [sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp],
                     [-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp]], np.float64)


def _vec(v, default=(0.0, 0.0, 0.0)) -> Tuple[float, float, float]:
    if isinstance(v, dict) and "X" in v:
        return (float(v.get("X", 0.0)), float(v.get("Y", 0.0)), float(v.get("Z", 0.0)))
    return default


def _rot(v) -> Tuple[int, int, int]:
    if isinstance(v, dict) and "Pitch" in v:
        return (int(v.get("Pitch", 0)), int(v.get("Yaw", 0)), int(v.get("Roll", 0)))
    return (0, 0, 0)


def _f32(x: float) -> float:
    return float(np.float32(x))


@dataclass
class Transform:
    location: Tuple[float, float, float] = (0.0, 0.0, 0.0)
    rotation: Tuple[int, int, int] = (0, 0, 0)
    scale: Tuple[float, float, float] = (1.0, 1.0, 1.0)
    pre_pivot: Tuple[float, float, float] = (0.0, 0.0, 0.0)

    def apply(self, pts: np.ndarray) -> np.ndarray:
        p = (np.asarray(pts, np.float64) - np.array(self.pre_pivot)) * np.array(self.scale)
        return p @ rotation_matrix(self.rotation) + np.array(self.location)


@dataclass
class MeshSets:
    asset: str
    positions: np.ndarray          # render vertices (per-poly triangles index these)
    pp_tris: np.ndarray            # (n, 4) v0, v1, v2, material index
    hull_verts: np.ndarray
    hull_tris: np.ndarray
    materials: List[Tuple[object, int]]  # per section: (owner package, material ref)
    use_simple_line: bool
    use_simple_box: bool
    vert_ranges: Dict[str, Tuple[int, int]] = field(default_factory=dict)


class CollisionBuilder:
    def __init__(self, src: MapSource):
        self.src = src
        self.cf = import_formats()
        self.d = C.CollisionData(sources=[], material_ext=[], cylinder_ext=[])
        self._materials: Dict[tuple, int] = {}
        self._material_props: Dict[tuple, Tuple[str, dict, str]] = {}
        self._meshes: Dict[tuple, Optional[MeshSets]] = {}
        self.stats: Dict[str, int] = {}
        self.zone_bounds: Dict[int, List[np.ndarray]] = {}

    def count(self, key: str, n: int = 1) -> None:
        self.stats[key] = self.stats.get(key, 0) + n

    # ---- materials ---------------------------------------------------------------------------

    def _material_info(self, owner, ref: int) -> Tuple[str, dict, str]:
        """(asset path, decoded props, class path) of a material reference stored in `owner`."""
        key = (str(owner.path) if owner is not None else "", ref)
        hit = self._material_props.get(key)
        if hit is not None:
            return hit
        cd = self.cf.classdefaults
        path = cd.object_path(owner, ref) if owner is not None and ref else ""
        props: dict = {}
        cls = ""
        mpkg, me = self.src.pset.resolve(owner, ref) if owner is not None and ref else (None, None)
        if (mpkg is None or me is None) and path:
            # Materials of the script packages (Engine.DefaultTexture, Engine.MenuBlack ...) are not under Packages/.
            top = path.split(".", 1)[0]
            spkg = self.src.db.universe.package(top) if top.lower() in {p.lower() for p in self.src.db.packages} \
                else None
            if spkg is not None:
                mpkg, me = spkg, self.src.db.universe.by_path[top.lower()].get(path.lower())
        if mpkg is not None and me is not None and me.size > 0:
            cls = self.src.db.find(f"{me.class_package}.{me.class_name}") or ""
            rec = self.src.db.record(cls) if cls else None
            if rec is not None:
                from scctpkg.properties import skip_state_frame  # noqa: PLC0415
                try:
                    r = mpkg.export_reader(me)
                    skip_state_frame(r, me.flags)
                    props = self.src.db.decoder.tagged(r, mpkg, rec["_field"])
                except Exception as ex:  # noqa: BLE001
                    self.src.notes.add("material-decode", f"{path}: {ex}")
        elif ref:
            self.src.notes.add("material-missing", path or str(ref))
        out = (path, props, cls)
        self._material_props[key] = out
        return out

    def material(self, owner, ref: int, poly_flags: int = 0) -> int:
        path, props, cls = self._material_info(owner, ref) if ref else ("", {}, "")
        db = self.src.db

        def val(name, default=0):
            if name in props:
                return props[name]
            return db.default(cls, name, default) if cls else default

        names = []
        if int(val("bClimbable", 0) or 0):
            names.append("climbable")
        if int(val("bNoSplitJump", 0) or 0):
            names.append("noSplitJump")
        if int(val("bNoStickyCam", 0) or 0):
            names.append("noStickyCam")
        if poly_flags & modeltail.PF_SEMISOLID:
            names.append("semisolid")
        if poly_flags & modeltail.PF_INVISIBLE:
            names.append("invisible")
        bits = 0
        for i, n in enumerate(("bHideInEMFVision", "bHideInThermalVision", "bEchelonLightThrough")):
            if val(n, False):
                bits |= 1 << i
        surface = int(val("SurfaceType", 0) or 0)
        occlusion = int(val("OcclusionType", 0) or 0)
        name_idx = self.d.string(path) if path else C.NONE
        key = (surface, C.flag_bits(names, C.MATERIAL_FLAGS), name_idx, poly_flags, bits, occlusion)
        idx = self._materials.get(key)
        if idx is None:
            idx = len(self.d.materials)
            self._materials[key] = idx
            self.d.materials.append(C.Material(surface, key[1], name_idx))
            self.d.material_ext.append(C.MaterialExt(poly_flags, bits, occlusion))
        return idx

    # ---- helpers -----------------------------------------------------------------------------

    def actor_flags(self, info: ActorInfo) -> int:
        names = [n for n, prop in FLAG_PROPS if self.src.flag(info, prop)]
        return C.flag_bits(names, C.COLLIDER_FLAGS)

    def transform(self, info: ActorInfo) -> Transform:
        v = lambda n, d=None: self.src.value(info, n, d)  # noqa: E731
        ds = float(v("DrawScale", 1.0) or 0.0)
        s3 = _vec(v("DrawScale3D"), (1.0, 1.0, 1.0))
        return Transform(_vec(v("Location")), _rot(v("Rotation")),
                         (_f32(ds * s3[0]), _f32(ds * s3[1]), _f32(ds * s3[2])), _vec(v("PrePivot")))

    def _collider(self, kind: str, flags: int, actor: Optional[str], vstart: int, vcount: int, tstart: int,
                  tcount: int, xf: Transform, world_pts: np.ndarray, source: C.ColliderSource,
                  parent: Optional[str] = None, bsp: Tuple[int, int] = (0, 0),
                  hulls: Tuple[int, int] = (0, 0)) -> int:
        if len(world_pts):
            lo = world_pts.min(axis=0)
            hi = world_pts.max(axis=0)
        else:
            lo = hi = np.array(xf.location, np.float64)
        aabb_min = tuple(float(np.float32(x)) for x in lo)
        aabb_max = tuple(float(np.float32(x)) for x in hi)
        aabb_min = tuple(min(a, b) for a, b in zip(aabb_min, aabb_max))
        self.d.colliders.append(C.Collider(
            kind=KIND[kind], flags=flags, actor=self.d.string(actor) if actor else C.NONE,
            parent=self.d.string(parent) if parent else C.NONE, vert_start=vstart, vert_count=vcount,
            tri_start=tstart, tri_count=tcount, bsp_start=bsp[0], bsp_count=bsp[1], hull_start=hulls[0],
            hull_count=hulls[1], location=xf.location, rotation=xf.rotation, scale=xf.scale, pre_pivot=xf.pre_pivot,
            aabb_min=aabb_min, aabb_max=aabb_max))
        self.d.sources.append(source)
        if len(self.d.colliders) > 0xFFFF:
            raise ValueError("more than 65535 colliders")
        self.count(f"colliders.{kind}")
        return len(self.d.colliders) - 1

    def _add_points(self, pts: np.ndarray) -> int:
        start = len(self.d.verts)
        self.d.verts.extend((float(p[0]), float(p[1]), float(p[2])) for p in np.asarray(pts, np.float32))
        return start

    # ---- BSP models --------------------------------------------------------------------------

    def _model_triangles(self, model: modeltail.Model, pkg, vstart: int, collider: int, solid_only: bool,
                         zones: bool) -> Tuple[List[Tuple[int, int]], List[np.ndarray]]:
        """Append the node polygons as triangles; returns per node (triStart, triCount) and the points used."""
        pts64 = model.points.astype(np.float64)
        spans = []
        used = []
        for n in model.nodes:
            start = len(self.d.tris)
            pool = model.polygon(n)
            surf = model.surfs[n.surf] if 0 <= n.surf < len(model.surfs) else None
            if surf is None or len(pool) < 3 or (solid_only and surf.poly_flags & (modeltail.PF_NOT_SOLID |
                                                                                   modeltail.PF_PORTAL)):
                spans.append((start, 0))
                if surf is not None and len(pool) >= 3:
                    self.count("bsp.nonSolidPolys")
                continue
            mat = self.material(pkg, surf.material, surf.poly_flags)
            normal = np.array(n.plane[:3], np.float64)
            for k in range(1, len(pool) - 1):
                a, b, c = int(pool[0]), int(pool[k]), int(pool[k + 1])
                cr = np.cross(pts64[b] - pts64[a], pts64[c] - pts64[a])
                if float(np.dot(cr, cr)) < DEGENERATE:
                    self.count("bsp.degenerateTris")
                    continue
                if float(np.dot(cr, normal)) < 0.0:
                    b, c = c, b
                self.d.tris.append((vstart + a, vstart + b, vstart + c, collider, mat))
            spans.append((start, len(self.d.tris) - start))
            poly = pts64[pool]
            used.append(poly)
            if zones:
                for z in (n.zone_front, n.zone_back):
                    if z:
                        self.zone_bounds.setdefault(z, []).append(poly)
        return spans, used

    def _append_nodes(self, model: modeltail.Model, pkg, spans: List[Tuple[int, int]]) -> Tuple[int, int]:
        bstart = len(self.d.bsp)
        for n, (ts, tc) in zip(model.nodes, spans):
            rel = lambda i: bstart + i if i >= 0 else -1  # noqa: E731
            self.d.bsp.append(C.BspNode(plane=tuple(float(x) for x in n.plane), front=rel(n.front),
                                        back=rel(n.back), coplanar=rel(n.coplanar),
                                        collision_bound=self._leaf_hull(model, pkg, n),
                                        tri_start=ts, tri_count=tc, node_flags=n.flags & 0xFFFF,
                                        zone_front=n.zone_front & 0xFF, zone_back=n.zone_back & 0xFF))
        return bstart, len(model.nodes)

    def _leaf_hull(self, model: modeltail.Model, pkg, node: modeltail.ModelNode) -> int:
        """HULL index of a node's collision bound (outside every collider's hull range), or -1."""
        if node.collision_bound < 0:
            return -1
        hull = modeltail.leaf_hull(model, node.collision_bound)
        if hull is None:
            self.count("bsp.leafHullsUnreadable")
            return -1
        surf = model.surfs[node.surf] if 0 <= node.surf < len(model.surfs) else None
        mat = self.material(pkg, surf.material, surf.poly_flags) if surf is not None else self.material(None, 0, 0)
        pstart = len(self.d.planes)
        self.d.planes.extend(hull.planes)
        self.d.hulls.append(C.Hull(pstart, len(hull.planes), 0, 0, mat, hull.box_min, hull.box_max))
        self.count("bsp.leafHulls")
        self.count("bsp.leafHullPlanes", len(hull.planes))
        return len(self.d.hulls) - 1

    def add_world(self) -> Optional[modeltail.Model]:
        src = self.src
        ref = src.header.model_a or src.header.model_b
        if not ref:
            src.notes.add("no-world-model", src.name)
            return None
        e = src.pkg.exports[ref - 1]
        model = modeltail.read_model(src.pkg, e)
        vstart = self._add_points(model.points)
        coll = len(self.d.colliders)
        tstart = len(self.d.tris)
        spans, used = self._model_triangles(model, src.pkg, vstart, coll, solid_only=True, zones=True)
        bsp = self._append_nodes(model, src.pkg, spans)
        pts = np.concatenate(used) if used else model.points.astype(np.float64)
        source = C.ColliderSource(self.d.string(e.name), self.d.string(f"{src.name}.{e.name}"), SET["worldBsp"], 1, 3)
        self._collider("worldBsp", WORLD_FLAGS, None, vstart, len(model.points), tstart, len(self.d.tris) - tstart,
                       Transform(), pts, source, bsp=bsp)
        self.count("bsp.nodes", len(model.nodes))
        self.count("bsp.triangles", len(self.d.tris) - tstart)
        return model

    def add_brush(self, info: ActorInfo) -> bool:
        brush = self.src.value(info, "Brush")
        if not isinstance(brush, Ref) or brush.ref <= 0 or not self.src.own(brush):
            return False
        e = self.src.pkg.exports[brush.ref - 1]
        if e.class_name != "Model":
            return False
        for s in ("MainScale", "PostScale"):
            sc = info.props.get(s)
            if isinstance(sc, dict) and _vec(sc.get("Scale"), (1.0, 1.0, 1.0)) != (1.0, 1.0, 1.0):
                self.src.notes.add("brush-csg-scale", f"{info.name}.{s}")
        model = modeltail.read_model(self.src.pkg, e)
        vstart = self._add_points(model.points)
        coll = len(self.d.colliders)
        tstart = len(self.d.tris)
        spans, used = self._model_triangles(model, self.src.pkg, vstart, coll, solid_only=False, zones=False)
        bsp = self._append_nodes(model, self.src.pkg, spans)
        hulls = self._convex_hull(model, vstart, used)
        xf = self.transform(info)
        pts = np.concatenate(used) if used else model.points.astype(np.float64)
        source = C.ColliderSource(self.d.string(info.name), self.d.string(f"{self.src.name}.{e.name}"),
                                  SET["brushModel"], 1, 3)
        self._collider("brush", self.actor_flags(info), info.name, vstart, len(model.points), tstart,
                       len(self.d.tris) - tstart, xf, xf.apply(pts) if len(pts) else pts, source, bsp=bsp, hulls=hulls)
        return True

    def _convex_hull(self, model: modeltail.Model, vstart: int, used: List[np.ndarray]) -> Tuple[int, int]:
        """One HULL from the brush's node planes when every polygon point is behind or on every plane."""
        if not used:
            return (0, 0)
        pts = np.concatenate(used)
        planes = []
        seen = set()
        for n in model.nodes:
            if n.num_verts < 3:
                continue
            key = tuple(np.float32(x) for x in n.plane)
            if key in seen:
                continue
            seen.add(key)
            planes.append(n.plane)
        if len(planes) < 4:
            return (0, 0)
        pl = np.array(planes, np.float64)
        dist = pts @ pl[:, :3].T - pl[:, 3]
        if dist.max() > CONVEX_EPS:
            self.count("brush.concave")
            return (0, 0)
        pstart = len(self.d.planes)
        self.d.planes.extend(tuple(float(x) for x in p) for p in planes)
        corners = np.unique(np.round(pts, 4), axis=0)
        cstart = self._add_points(corners)
        lo, hi = corners.min(axis=0), corners.max(axis=0)
        mat = self.material(None, 0, 0)
        self.d.hulls.append(C.Hull(pstart, len(planes), cstart, len(corners), mat,
                                   tuple(float(np.float32(x)) for x in lo), tuple(float(np.float32(x)) for x in hi)))
        return (len(self.d.hulls) - 1, 1)

    # ---- static meshes -----------------------------------------------------------------------

    def mesh_sets(self, ref: Ref) -> Optional[MeshSets]:
        owner, e = self.src.ref_export(ref)
        if owner is None or e is None or e.class_name != "StaticMesh":
            self.src.notes.add("mesh-missing", ref.path if isinstance(ref, Ref) else str(ref))
            return None
        key = (str(owner.path), e.index)
        if key in self._meshes:
            return self._meshes[key]
        try:
            m = self.cf.smcollision.read_mesh(owner, e)
        except Exception as ex:  # noqa: BLE001
            self.src.notes.add("mesh-collision", f"{e.name}: {ex}")
            self._meshes[key] = None
            return None
        mats = []
        raw_mats = m.props.get("Materials")
        if isinstance(raw_mats, list):
            for x in raw_mats:
                mats.append((owner, int(x.get("Material", 0) or 0) if isinstance(x, dict) else 0))
        flags = dict(STATICMESH_DEFAULTS)
        for k in flags:
            if k in m.props:
                flags[k] = bool(m.props[k])
        c = m.collision
        sets = MeshSets(self.cf.classdefaults.export_path(owner, e), m.positions, c.pp_tris.astype(np.int64),
                        c.hull_verts[:, :3].copy(), c.hull_tris.astype(np.int64), mats,
                        flags["UseSimpleLineCollision"], flags["UseSimpleBoxCollision"])
        self._meshes[key] = sets
        return sets

    def _section_material(self, info: ActorInfo, sets: MeshSets, section: int) -> int:
        skins = self.src.value(info, "Skins")
        if isinstance(skins, list) and section < len(skins) and isinstance(skins[section], Ref) and skins[section].ref:
            skin = skins[section]
            if self.src.own(skin):
                return self.material(self.src.pkg, skin.ref, 0)
            owner, e = self.src.find_path(skin.path)
            if owner is not None and e is not None:
                return self.material(owner, e.index + 1, 0)
        if 0 <= section < len(sets.materials):
            owner, mref = sets.materials[section]
            return self.material(owner, mref, 0)
        return self.material(None, 0, 0)

    def _first_section(self, sets: MeshSets) -> int:
        for i, (_, mref) in enumerate(sets.materials):
            if mref:
                return i
        return 0

    def mesh_variants(self, info: ActorInfo) -> List[Tuple[int, Ref]]:
        """(variant bit, mesh) pairs: the initial mesh, then for gameplay objects the triggered and damaged ones."""
        out = []
        sm = self.src.value(info, "StaticMesh")
        if isinstance(sm, Ref) and sm.ref:
            out.append((1, sm))
        if self.src.db.is_a(info.cls, "SBase.SGamePlayObject"):
            tm = self.src.value(info, "TriggeredMesh")
            if isinstance(tm, Ref) and tm.ref:
                out.append((2, tm))
            stages = self.src.value(info, "DamagedMeshes")
            if isinstance(stages, list):
                for i, st in enumerate(stages[:14]):
                    if isinstance(st, dict) and isinstance(st.get("StaticMesh"), Ref) and st["StaticMesh"].ref \
                            and not st.get("bDisableCollision", False):
                        out.append((4 << i, st["StaticMesh"]))
        return out

    def kind_of(self, info: ActorInfo) -> Tuple[Optional[str], Optional[str]]:
        """(collider kind or None for meshPoly/meshHull, parent actor id)."""
        db = self.src.db
        if db.is_a(info.cls, "Engine.Mover"):
            return "mover", None
        if db.is_a(info.cls, "SBase.SVitre"):
            return "glass", None
        # Gameplay objects (cameras, sensors, lasers, breakers ...) are not world geometry: traces see them as other
        # actors (bullets from a pawn hit them) and their colliders follow the actor (a camera head turns).
        if db.is_a(info.cls, "SBase.SBasicGamePlayObject") and not self.src.flag(info, "bWorldGeometry"):
            return "gpo", None
        base = self.src.value(info, "Base")
        if isinstance(base, Ref) and base.ref > 0:
            b = self.src.by_ref.get(base.ref)
            if b is not None and b.category == "gameplay" and (db.is_a(b.cls, "Engine.Mover") or
                                                             db.is_a(b.cls, "SBase.SAnimatedMesh")):
                return "attached", b.name
        return None, None

    def static_mesh_primitive(self, info: ActorInfo) -> bool:
        src = self.src
        if isinstance(src.value(info, "Brush"), Ref) and src.value(info, "Brush").ref:
            return False
        if int(src.value(info, "DrawType", 0) or 0) != DT_STATICMESH:
            return False
        if src.flag(info, "bUseCylinderCollision"):
            return False
        sm = src.value(info, "StaticMesh")
        return isinstance(sm, Ref) and sm.ref != 0

    def add_mesh_actor(self, info: ActorInfo) -> bool:
        src = self.src
        if not src.flag(info, "bCollideActors"):
            self.count("meshActors.notColliding")
            return False
        kind, parent = self.kind_of(info)
        actor_id = info.name if info.category == "gameplay" else None
        if actor_id is None and kind in ("mover", "glass", "gpo"):
            kind = None
        flags = self.actor_flags(info)
        xf = self.transform(info)
        variants: Dict[str, Tuple[int, Ref]] = {}
        for bit, mesh in self.mesh_variants(info):
            key = mesh.path.lower()
            if key in variants:
                variants[key] = (variants[key][0] | bit, variants[key][1])
            else:
                variants[key] = (bit, mesh)
        added = False
        for bits, mesh in variants.values():
            sets = self.mesh_sets(mesh)
            if sets is None:
                continue
            has_hull = len(sets.hull_tris) > 0
            zero_hull = has_hull and sets.use_simple_line
            box_hull = has_hull and sets.use_simple_box
            for set_name, verts, tris in (("perPoly", sets.positions, sets.pp_tris),
                                          ("simple", sets.hull_verts, sets.hull_tris)):
                if len(tris) == 0:
                    continue
                if set_name not in sets.vert_ranges:
                    sets.vert_ranges[set_name] = (self._add_points(verts), len(verts))
                vstart, vcount = sets.vert_ranges[set_name]
                coll = len(self.d.colliders)
                tstart = len(self.d.tris)
                first = self._first_section(sets)
                cache: Dict[int, int] = {}
                for v0, v1, v2, m in tris:
                    sec = int(m) if set_name == "perPoly" or int(m) < len(sets.materials) else first
                    mat = cache.get(sec)
                    if mat is None:
                        mat = cache[sec] = self._section_material(info, sets, sec)
                    # Meshes wind (v1 - v0) x (v2 - v0) inward (C2 spike): swap to the SVMap front-face rule.
                    self.d.tris.append((vstart + int(v0), vstart + int(v2), vstart + int(v1), coll, mat))
                use = 0
                if (set_name == "simple") == zero_hull:
                    use |= 1
                if (set_name == "simple") == box_hull:
                    use |= 2
                used = np.unique(tris[:, :3].reshape(-1))
                world = xf.apply(np.asarray(verts, np.float64)[used])
                ckind = kind or ("meshPoly" if set_name == "perPoly" else "meshHull")
                cflags = flags | (0 if bits & 1 else STARTS_DISABLED)
                source = C.ColliderSource(self.d.string(info.name), self.d.string(sets.asset),
                                          SET[set_name], bits, use)
                self._collider(ckind, cflags, actor_id, vstart, vcount, tstart, len(tris), xf, world, source,
                               parent=parent)
                self.count(f"triangles.{set_name}", len(tris))
                added = True
        return added

    # ---- cylinders and zones -----------------------------------------------------------------

    def add_cylinder(self, info: ActorInfo, kind: int = 0, radius: Optional[float] = None,
                     height: Optional[float] = None, flags: Optional[int] = None, cls: str = "") -> None:
        src = self.src
        r = float(src.value(info, "CollisionRadius", 0.0) or 0.0) if radius is None else radius
        h = float(src.value(info, "CollisionHeight", 0.0) or 0.0) if height is None else height
        self.d.cylinders.append(C.Cylinder(self.d.string(info.name), self.actor_flags(info) if flags is None else flags,
                                           _vec(src.value(info, "Location")), _f32(r), _f32(h)))
        self.d.cylinder_ext.append(C.CylinderExt(kind, self.d.string(cls) if cls else C.NONE))
        self.count("cylinders." + C.CYLINDER_KINDS[kind])

    def add_interaction(self, info: ActorInfo) -> None:
        src = self.src
        ic = src.value(info, "InteractionClass")
        if not isinstance(ic, Ref) or ic.ref == 0 or not src.db.is_a(info.cls, "SBase.SBasicGamePlayObject"):
            return
        radius = float(src.value(info, "fInteractionRadius", -1.0) or -1.0)
        height = float(src.value(info, "fInteractionHeight", -1.0) or -1.0)
        if radius < 0:
            radius = float(src.value(info, "CollisionRadius", 0.0) or 0.0) + 150.0
        if height < 0:
            height = float(src.value(info, "CollisionHeight", 0.0) or 0.0) + 150.0
        self.add_cylinder(info, 1, radius, height, C.flag_bits(["collideActors"], C.COLLIDER_FLAGS), ic.path)

    def add_zones(self, model: Optional[modeltail.Model]) -> None:
        src = self.src
        zones = model.zones if model is not None else []
        if not zones:
            zones = [modeltail.ModelZone(0, 0, 0)]
        for i, z in enumerate(zones):
            if i == 0:
                actor = src.level_info.name
            else:
                info = src.by_ref.get(z.actor) if z.actor > 0 else None
                actor = info.name if info is not None and info.category in ("gameplay", "level") else None
            sky = 0
            if actor and i > 0:
                info = src.by_ref.get(z.actor)
                if info is not None and src.db.is_a(info.cls, "Engine.SkyZoneInfo"):
                    sky = 1
            polys = self.zone_bounds.get(i)
            if polys:
                pts = np.concatenate(polys)
                lo = tuple(float(np.float32(x)) for x in pts.min(axis=0))
                hi = tuple(float(np.float32(x)) for x in pts.max(axis=0))
            else:
                lo = hi = (0.0, 0.0, 0.0)
            self.d.zones.append(C.Zone(self.d.string(actor) if actor else C.NONE, sky, lo, hi))

    # ---- whole map ---------------------------------------------------------------------------

    def build(self) -> bytes:
        src = self.src
        world = self.add_world()
        for info in src.actors:
            if info.category not in ("gameplay", "render"):
                continue
            if info.category == "gameplay" and self.add_brush(info):
                continue
            if self.static_mesh_primitive(info):
                self.add_mesh_actor(info)
                continue
            if info.category == "gameplay" and src.flag(info, "bCollideActors"):
                self.add_cylinder(info)
        for info in src.of("gameplay"):
            self.add_interaction(info)
        self.add_zones(world)
        self.count("zones", len(self.d.zones))
        self.count("materials", len(self.d.materials))
        self.count("vertices", len(self.d.verts))
        self.count("triangles", len(self.d.tris))
        self.count("bspNodes", len(self.d.bsp))
        self.count("hulls", len(self.d.hulls))
        return C.write_collision(self.d)
