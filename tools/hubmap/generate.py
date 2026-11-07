"""Generate the Hub, the OpenSCCT social hub map, as a native SVMap folder, and its tracked area manifest.

    python -B tools/hubmap/generate.py                 fixtures/hub and fixtures/hubmap/areas.json
    python -B tools/hubmap/generate.py --out DIR       the map folder somewhere else
    python -B tools/hubmap/generate.py --check         fail when the tracked folder or manifest is not what this writes

The layout is tools/hubmap/layout.py; the Hub is an OpenSCCT addition (spec/additions/hub.md). The world is built
the way TestRange's is (tools/testmap: the boundary of the carved hall less the solids, a solid-leaf BSP over its
polygons and vertex-lit render meshes), with credited scanned textures and glTF scenery. The folder is built from
the layout and the tracked source-asset cache. No game files or network are read. Output is byte-identical on every run.
"""

from __future__ import annotations

import argparse
import importlib.util
import math
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
ASSETS = REPO / "fixtures" / "hub-assets"
TESTMAP = REPO / "tools" / "testmap"
sys.path.insert(0, str(REPO / "content"))


def _load(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


# TestRange's generator (faces, the glTF writer, textures) with its own bsp and layout modules; the hub layout is
# loaded under its own name so the two layouts never meet.
sys.path.insert(0, str(TESTMAP))
try:
    T = _load("testmap_generate", TESTMAP / "generate.py")
    import bsp as B  # noqa: E402
finally:
    sys.path.remove(str(TESTMAP))
L = _load("hub_layout", HERE / "layout.py")
A = _load("hub_art", HERE / "art.py")

from svmbuild import canonical  # noqa: E402
from svmbuild.svmap import with_derived, write_svmap  # noqa: E402
from svmbuild.writers import collision as C  # noqa: E402

GENERATOR = "tools.hubmap"
GENERATOR_VERSION = 3
MAP_FORMAT_VERSION = '1.2.0' # optional moving-GE attachments; older Hub manifests are still readable
DEFAULT_OUT = REPO / "fixtures" / "hub"
MANIFEST = REPO / "fixtures" / "hubmap" / "areas.json"
SUBDIVIDE = 512.0          # render cells: coarser than TestRange's 128, the hall is 12288 wide
WORLD_FLAGS = T.WORLD_FLAGS

Vec3 = tuple[float, float, float]


def world_volumes() -> tuple[list[B.Convex], list[B.Convex]]:
    faces = T.box_faces(L.HALL_LO, L.HALL_HI, "skyline")
    for f in faces:
        nz = B.plane_of(f.corners).n[2]
        # The carve's faces point out of the hall: the floor's normal is down, the ceiling's up.
        f.material = "roof" if nz < -0.5 else "sky" if nz > 0.5 else "skyline"
    carves = [T._convex(faces, 0)]
    for lake in L.LAKES:
        cx,cy=lake['centre']
        top=[(x,y,1) for x,y in lake['shore']]
        bottom=[(cx+(x-cx)*.72,cy+(y-cy)*.72,-lake['depth']) for x,y in lake['shore']]
        fs=[T.Face(list(reversed(bottom)), 'ruin_moss'),T.Face(top,'ruin_moss')]
        for i in range(len(top)):
            j=(i+1)%len(top)
            fs.append(T.Face([bottom[i],bottom[j],top[j],top[i]],'ruin_moss'))
        carves.append(T._convex(fs,-len(carves)))
    solids = []
    for j, s in enumerate(L.SOLIDS):
        fs = T.wedge_faces(s.lo, s.hi, s.up, s.material) if s.up else T.box_faces(s.lo, s.hi, s.material)
        solids.append(T._convex(fs, 1 + j))
    return carves, solids


def world_polys() -> list[B.Poly]:
    carves, solids = world_volumes()
    def cut_carve(poly, index):
        parts=[poly]
        for other in carves:
            if other.index!=index:parts=B._subtract(parts,other,keep_same=False)
        return parts
    return B.boundary(carves, solids, cut_carve, spatial=True)


def build_gameplay() -> dict:
    actors = []
    for a in L.ACTORS:
        props = {"Location": T._f(a.location)}
        if a.yaw or a.pitch:
            props["Rotation"] = T._rot(a.yaw, a.pitch)
        props.update(a.props)
        actors.append({"id": a.id, "class": a.cls, "props": props})
    return with_derived({"formatVersion": MAP_FORMAT_VERSION, "map": L.MAP_ID,
                         "levelInfo": {"id": "LevelInfo0", "class": "Engine.LevelInfo", "props": dict(L.LEVEL_INFO)},
                         "mapSettings": L.MAP_SETTINGS, "actors": actors})


def build_traversal() -> dict:
    ges = [{"id": g.id, "kind": g.kind, "points": [[float(c) for c in p] for p in g.points], "mask": 0, "platform": 0,
            "src": {"kind": "synthetic", "note": g.note}, "activeAtStart": True} for g in L.GES]
    for mover in L.MOVERS:
        x,y,z=mover['half']
        for face,a,b in (('S',(-x,-y,z),(x,-y,z)),('N',(x,y,z),(-x,y,z)),
                         ('W',(-x,y,z),(-x,-y,z)),('E',(x,-y,z),(x,y,z))):
            ges.append({'id':mover['id']+'LG'+face,'kind':'LG','points':[list(a),list(b)],'mask':0,'platform':0,
                'attachedTo':mover['id'],'src':{'kind':'synthetic','note':'moving platform perimeter'},'activeAtStart':True})
    return {"formatVersion": MAP_FORMAT_VERSION, "map": L.MAP_ID, "activationMask": L.LEVEL_INFO["GEActivationMask"], "ges": ges}


def build_collision(polys: list[B.Poly]) -> bytes:
    d = C.CollisionData(sources=[], material_ext=[], cylinder_ext=[])
    mats = {}
    for key, m in L.MATERIALS.items():
        mats[key] = len(d.materials)
        d.materials.append(C.Material(m.surface, C.flag_bits(["climbable"] if m.climbable else [], C.MATERIAL_FLAGS),
                                      d.string(f"Hub.Materials.{key}")))
        d.material_ext.append(C.MaterialExt(0, 0, m.occlusion))
    vert_index: dict[Vec3, int] = {}

    def add_poly(verts, material):
        idx = []
        for p in verts:
            key = tuple(canonical.to_f32(c) for c in p)
            if key not in vert_index:
                vert_index[key] = len(d.verts)
                d.verts.append(key)
            idx.append(vert_index[key])
        for i in range(1, len(idx) - 1):
            d.tris.append((idx[0], idx[i], idx[i + 1], 0, mats[material]))

    # One zone: no zone planes, the whole tree is zone 1.
    root = B.build(polys, [], 1, balance=0.25)
    for plane, front, back, node_polys, csg, zf, zb in B.flatten(root):
        t0 = len(d.tris)
        for q in node_polys:
            add_poly(q.verts, q.material)
        d.bsp.append(C.BspNode((float(plane.n[0]), float(plane.n[1]), float(plane.n[2]), float(plane.d)), front, back, -1, -1,
                               t0, len(d.tris) - t0, 0 if csg else B.NOT_CSG, zf, zb))
    lo, hi = T._aabb(d.verts)
    d.colliders.append(C.Collider(kind=C.KINDS.index("worldBsp"), flags=C.flag_bits(WORLD_FLAGS, C.COLLIDER_FLAGS),
                                  vert_start=0, vert_count=len(d.verts), tri_start=0, tri_count=len(d.tris),
                                  bsp_start=0, bsp_count=len(d.bsp), aabb_min=lo, aabb_max=hi))
    d.sources.append(C.ColliderSource(source=d.string(L.MAP_ID + ".World"), set=C.SOURCE_SETS.index("worldBsp")))
    d.zones.append(C.Zone(d.string("LevelInfo0"), 0, lo, hi))
    d.zones.append(C.Zone(d.string("ZoneHall"), 0, (L.HALL_LO[0], L.HALL_LO[1], -128.0), L.HALL_HI))
    # Moving platforms own local hulls; their geometry must never enter the static BSP.
    for mover in L.MOVERS:
        x,y,z = mover["half"]
        low,high=(-x,-y,-z),(x,y,z)
        v0,p0,h0=len(d.verts),len(d.planes),len(d.hulls)
        d.verts.extend(T._box_corners(low,high))
        d.planes.extend(T._box_planes(low,high))
        d.hulls.append(C.Hull(p0,6,v0,8,mats[mover["material"]],low,high))
        at=mover["at"]
        d.colliders.append(C.Collider(kind=C.KINDS.index("mover"),flags=C.flag_bits(WORLD_FLAGS,C.COLLIDER_FLAGS),
            actor=d.string(mover["id"]),vert_start=v0,vert_count=8,hull_start=h0,hull_count=1,location=at,
            aabb_min=tuple(at[i]+low[i] for i in range(3)),aabb_max=tuple(at[i]+high[i] for i in range(3))))
        d.sources.append(C.ColliderSource(source=d.string(mover["id"]),asset=d.string("Hub."+mover["id"]),set=C.SOURCE_SETS.index("simple")))
    # Shared transformed source geometry, also used to locate the model's traversal rims.
    for model in L.SCENERY_COLLISION:
        ci=len(d.colliders);v0=len(d.verts);t0=len(d.tris)
        d.verts.extend(model['vertices'])
        material=mats[model.get('material','bark' if '-tree-' in model['asset'] else 'concrete')]
        d.tris.extend((v0+a,v0+b,v0+c,ci,material) for a,b,c in model['triangles'])
        low,high=T._aabb(model['vertices'])
        d.colliders.append(C.Collider(kind=C.KINDS.index('meshPoly'),flags=C.flag_bits(WORLD_FLAGS,C.COLLIDER_FLAGS),
            vert_start=v0,vert_count=len(model['vertices']),tri_start=t0,tri_count=len(model['triangles']),aabb_min=low,aabb_max=high))
        d.sources.append(C.ColliderSource(source=d.string(model['id']),asset=d.string(model['asset']),set=C.SOURCE_SETS.index('perPoly')))
    return C.write_collision(d)


# ---- render ---------------------------------------------------------------------------------------------------

def light_colour(li) -> tuple[float, float, float]:
    """FGetHSV(hue, saturation, 255) / 255 as the game reads it (ActorLighting.Colour): a hue triangle pulled to white."""
    h = li.hue
    if h < 86:
        hue = ((85 - h) / 85, h / 85, 0.0)
    elif h < 171:
        hue = (0.0, (170 - h) / 85, (h - 85) / 85)
    else:
        hue = ((h - 170) / 85, 0.0, (255 - h) / 84)
    s = li.saturation / 255
    return tuple(c + s * (1 - c) for c in hue)


MAP_LIGHTS = [li for li in L.LIGHTS if li.kit is None]
LIGHT_CELLS = {}
for light in L.LIGHTS:
    reach = 25.0 * (light.radius + 1.0)
    ranges = [range(math.floor((v-reach)/4096), math.floor((v+reach)/4096)+1) for v in light.location]
    for x in ranges[0]:
        for y in ranges[1]:
            for z in ranges[2]: LIGHT_CELLS.setdefault((x,y,z), []).append(light)


def light_at(p: Vec3, n: Vec3, kit=None) -> tuple[float, float, float]:
    """Zone ambient plus every map light (and a theme kit's own lights), the actor lighting model as TestRange bakes it."""
    a = L.ZONE_AMBIENT / 255.0
    total = [a, a, a]
    for li in LIGHT_CELLS.get(tuple(math.floor(v/4096) for v in p), ()):
        if li.kit is not None and li.kit != kit:
            continue
        dv = T._sub(li.location, p)
        dist = math.sqrt(T._dot(dv, dv))
        reach = 25.0 * (li.radius + 1.0)
        if dist >= reach or dist < 1e-6:
            continue
        nl = max(0.0, T._dot(n, (dv[0] / dist, dv[1] / dist, dv[2] / dist)))
        k = 2.0 * T.WHITE * li.brightness / 255.0 * T._smooth(dist / reach) * nl
        if k <= 0.0:
            continue
        c = light_colour(li)
        for i in range(3):
            total[i] += k * c[i]
    return tuple(min(2.0, t) for t in total)


def _cells(corners, uvs):
    """Splits all polygons into bounded cells, including the triangles left by BSP clipping."""
    if len(corners) != 4:
        def triangles(v, uv):
            lengths=[math.dist(v[i],v[(i+1)%3]) for i in range(3)]
            edge=max(range(3),key=lambda i:lengths[i])
            if lengths[edge]<=SUBDIVIDE:
                yield v,uv
                return
            a,b,c=edge,(edge+1)%3,(edge+2)%3
            mid=tuple((v[a][i]+v[b][i])/2 for i in range(3))
            mu=tuple((uv[a][i]+uv[b][i])/2 for i in range(2)) if uv else None
            yield from triangles([v[a],mid,v[c]], [uv[a],mu,uv[c]] if uv else None)
            yield from triangles([mid,v[b],v[c]], [mu,uv[b],uv[c]] if uv else None)
        for i in range(1,len(corners)-1):
            yield from triangles([corners[0],corners[i],corners[i+1]], [uvs[0],uvs[i],uvs[i+1]] if uvs else None)
        return
    a, b, c, dd = corners
    nu = max(1, math.ceil(max(math.dist(a, b), math.dist(dd, c)) / SUBDIVIDE))
    nv = max(1, math.ceil(max(math.dist(a, dd), math.dist(b, c)) / SUBDIVIDE))

    def lerp(q, s, t):
        top = tuple(q[0][k] + (q[1][k] - q[0][k]) * s for k in range(len(q[0])))
        bot = tuple(q[3][k] + (q[2][k] - q[3][k]) * s for k in range(len(q[0])))
        return tuple(top[k] + (bot[k] - top[k]) * t for k in range(len(q[0])))
    for i in range(nu):
        for j in range(nv):
            st = [(i / nu, j / nv), ((i + 1) / nu, j / nv), ((i + 1) / nu, (j + 1) / nv), (i / nu, (j + 1) / nv)]
            yield [lerp(corners, s, t) for s, t in st], ([lerp(uvs, s, t) for s, t in st] if uvs else None)


def world_uv(p: Vec3, n: Vec3, tile) -> tuple[float, float]:
    tu, tv = tile
    ax = max(range(3), key=lambda k: abs(n[k]))
    if ax == 2:
        return p[0] / tu, p[1] / tv
    if ax == 0:
        return p[1] / tu, -p[2] / tv
    return p[0] / tu, -p[2] / tv


def mesh_arrays(polys, kit=None):
    """polys: (corners, material, uvs or None). Baked vertex light, white for unlit materials."""
    pos, nrm, uv, col, idx = [], [], [], [], []
    for corners, material, uvs in polys:
        m = L.MATERIALS[material]
        v = corners
        n = T._cross(T._sub(v[1], v[0]), T._sub(v[2], v[0]))
        length = math.sqrt(T._dot(n, n))
        n = (n[0] / length, n[1] / length, n[2] / length)
        for cell, cuv in _cells(corners, uvs):
            base = len(pos)
            for k, p in enumerate(cell):
                pos.append(p)
                nrm.append(n)
                uv.append(cuv[k] if cuv else world_uv(p, n, m.tile))
                if m.unlit:
                    col.append((255, 255, 255, 255))
                else:
                    light = light_at(p, n, kit)
                    if material in ('roof','turf','ruin_moss'):
                        # Match the green understory at distance, where individual
                        # grass cards fade out. Keep the source photograph intact.
                        light = tuple(c*t for c,t in zip(light,(.70,.95,.52)))
                    col.append(tuple(int(round(min(1.0, c / 2.0) * 255)) for c in light) + (255,))
            for i in range(1, len(cell) - 1):
                idx.append((base, base + i, base + i + 1))
    return pos, nrm, uv, col, idx


def _split_floor(faces: list[T.Face]) -> list[T.Face]:
    """The hall floor inside each floor zone gets that zone's material (render only; all of it collides as the roof)."""
    out = []
    for f in faces:
        n = B.plane_of(f.corners).n
        centre = tuple(sum(c[i] for c in f.corners) / len(f.corners) for i in range(3))
        if f.material == "roof" and n[2] > 0.5 and abs(centre[2]) < 1e-6:
            rest = [B.Poly(list(f.corners), B.plane_of(f.corners), f.material, 0)]
            for lo, hi, material in L.FLOOR_ZONES:
                kept = []
                for poly in rest:
                    inside, outside = _clip_rect(poly, lo, hi)
                    out += [T.Face(list(q.verts), material) for q in inside]
                    kept += outside
                rest = kept
            out += [T.Face(list(q.verts), "roof") for q in rest]
        else:
            out.append(f)
    return out


def _clip_rect(poly: B.Poly, lo, hi):
    (x0, y0), (x1, y1) = lo, hi
    inside = [poly]
    outside = []
    for plane in (B.Plane((1.0, 0.0, 0.0), x0), B.Plane((-1.0, 0.0, 0.0), -x1),
                  B.Plane((0.0, 1.0, 0.0), y0), B.Plane((0.0, -1.0, 0.0), -y1)):
        keep = []
        for q in inside:
            front, back = B.split(q, plane)
            if back is not None:
                outside.append(back)
            if front is not None:
                keep.append(front)
        inside = keep
    return inside, outside


def _texture_bytes(key: str) -> bytes:
    photos = {'roof':'leafy_grass','turf':'leafy_grass','ruin_moss':'leafy_grass',
              'ruin_stone':'mossy_stone_wall','frozen_stone':'concrete_floor_worn_02','frozen_ice':'snow_02',
              'wood':'weathered_planks','ruin_wood':'weathered_planks','deck':'weathered_planks',
              'plaza':'grassy_cobblestone','concrete':'concrete_floor_worn_02','track':'concrete_floor_worn_02',
              'booth':'weathered_planks','steel_concrete':'concrete_floor_worn_02'}
    if key in photos:
        import io
        from PIL import Image
        out=io.BytesIO()
        Image.open(ASSETS / (photos[key] + '.jpg')).save(out, format='PNG')
        return out.getvalue()
    m = L.MATERIALS[key]
    if m.art in L.ART:
        fn, args, kwargs = L.ART[m.art]
        return getattr(A, fn)(*args, **kwargs)
    return A.texture(m.art)


def build_render(faces: list[T.Face]) -> dict[str, bytes]:
    files: dict[str, bytes] = {}
    glb = T.Glb()
    glb.g["asset"]["generator"] = f"{GENERATOR} {GENERATOR_VERSION}"
    materials, gameplay_info = {}, {}
    library, instances = [], []

    def add_material(key):
        name = f"Hub.Materials.{key}"
        if name not in materials:
            m = L.MATERIALS[key]
            uri = f"textures/Hub_{key}.png"
            files[uri] = _texture_bytes(key)
            flags = {"masked": False, "texture": uri, "translucent": False, "two_sided": False, "unlit": m.unlit}
            if m.uv_anim:
                flags["uv_anim"] = dict(m.uv_anim)
            materials[name] = {"flags": flags, "index": glb.material(name, uri), "source": name}
            info = {"OcclusionType": m.occlusion, "SurfaceType": m.surface}
            if m.climbable:
                info["bClimbable"] = 1
            gameplay_info[name] = info
        return materials[name]["index"]

    def add_instance(name, mesh, unlit=False, gameplay=False, hidden=False):
        lib_name = f"Hub.{name}#vc"
        library.append(glb.node(lib_name, mesh=mesh, extras={"svmap": {"asset": f"Hub.{name}", "role": "libraryMesh",
                                                                     "variant": "vc"}}))
        inst = {"actor": name, "class": "StaticMeshActor", "gameplay": gameplay, "hidden": hidden,
                "kind": "staticmesh", "location": [0.0, 0.0, 0.0], "mesh": lib_name,
                "rotation": {"Pitch": 0, "Roll": 0, "Yaw": 0}, "scale": [1.0, 1.0, 1.0], "vertexLit": not unlit}
        if unlit:
            inst["unlit"] = True
        instances.append(inst)

    def chunks(polys):
        buckets = {}
        for corners,key,uv in polys:
            for cell,cuv in _cells(corners,uv):
                centre=tuple(sum(p[i] for p in cell)/len(cell) for i in range(3))
                chunk=tuple(math.floor(v/4096) for v in centre)
                buckets.setdefault((key,chunk),[]).append((cell,key,cuv))
        return buckets

    world=[(f.corners,f.material,None) for f in _split_floor(faces) if not L.MATERIALS[f.material].hidden]
    for (key,chunk),polys in sorted(chunks(world).items()):
        name="World_"+key+"_"+"_".join(map(str,chunk))
        add_instance(name,glb.mesh("Hub_"+name,*mesh_arrays(polys),material=add_material(key)),unlit=L.MATERIALS[key].unlit)

    for gname,g in L.GROUPS.items():
        for (key,chunk),polys in sorted(chunks([(p.corners,p.material,p.uvs) for p in g.pieces if not L.MATERIALS[p.material].hidden]).items()):
            name=gname+"_"+key+"_"+"_".join(map(str,chunk))
            mesh=glb.mesh("Hub_"+name,*mesh_arrays(polys,g.kit),material=add_material(key))
            add_instance(name,mesh,unlit=L.MATERIALS[key].unlit,gameplay=g.kit is not None,
                         hidden=g.kit is not None and g.kit[1]!=L.PAD_DEFAULT_THEMES[g.kit[0]])

    for mover in L.MOVERS:
        x,y,z=mover["half"];key=mover["material"];name=mover["id"]
        faces=T.box_faces((-x,-y,-z),(x,y,z),key)
        mesh=glb.mesh("Hub_"+name,*mesh_arrays([(f.corners,key,None) for f in faces]),material=add_material(key))
        add_instance(name,mesh,gameplay=True)
        instances[-1].update(kind="mover",**{"class":"Mover"},location=list(mover["at"]))

    glb.node("Library", children=library, extras={"svmap": {"role": "library"}}, root=True)
    files["world.glb"] = glb.to_bytes({"svmap": {"map": L.MAP_ID, "instances": instances}})
    files["materials.json"] = canonical.dump_file({
        "materials": materials, "graph": {}, "gameplay": gameplay_info, "additive": [], "modulate": [],
        "lightmapped": [], "authoredColorMeshes": [],
        "vertexLitMeshes": [{"base": i["mesh"].split("#")[0], "name": i["mesh"]} for i in instances if i["vertexLit"]]})
    files["lighting.json"] = canonical.dump_file(build_lighting())
    files["scenery.json"] = canonical.dump_file({'format':1,'instances':L.SCENERY})
    # Compact numeric arrays keep the dense soft foliage manifest small.
    import json
    files['foliage.json'] = (json.dumps(L.FOLIAGE,separators=(',',':'))+'\n').encode()
    for asset in sorted(ASSETS.iterdir()):
        if asset.suffix in ('.glb', '.hdr', '.txt', '.json', '.png'):
            data = asset.read_bytes()
            if asset.suffix in ('.txt', '.json'):
                data = data.replace(b'\r\n', b'\n')
            files['scenery/' + asset.name] = data
    return files


def build_lighting() -> dict:
    lights = []
    for li in MAP_LIGHTS:
        lights.append({
            "apply_ingame": True, "apply_static": True, "brightness": float(li.brightness), "class": "Light",
            "color": [round(c, 4) for c in light_colour(li)], "cone": 128, "dynamic": False, "echelon_range": 0.0,
            "effect": 0, "gameplay": False, "hidden": False, "hue": li.hue, "initially_on": True, "kind": "light",
            "light_period": 32, "light_phase": 0, "light_type": "point", "location": [float(c) for c in li.location],
            "ltype": 1, "name": li.id, "near_plane_ratio": 0.1, "own_zone_only": True,
            "props": {"LightBrightness": float(li.brightness), "LightHue": li.hue, "LightRadius": float(li.radius),
                      "LightSaturation": li.saturation, "Location": T._f(li.location), "Tag": "Light",
                      "bAffectOwnZoneOnly": True, "bApplyToInGameLighting": True},
            "radius": float(li.radius), "rotation": [0, 0, 0], "saturation": li.saturation, "scale": [1.0, 1.0, 1.0],
            "spot_height": 0.0, "spot_width": 0.0, "tag": "Light", "triggered": False, "zone": "ZoneHall"})
    marker = next(a for a in L.ACTORS if a.id == "ZoneHall").location
    a = int(round(L.ZONE_AMBIENT * T.WHITE))
    fog = L.FOG
    zones = [{"ambient": [a, a, a, 255], "class": "ZoneInfo", "fog": True, "fog_color": list(fog["color"]) + [255],
              "fog_end": fog["end"], "fog_start": fog["start"], "hidden": True, "kind": "zone", "location": list(marker),
              "location_name": "Hub", "name": "ZoneHall",
              "props": {"AmbientBrightness": L.ZONE_AMBIENT, "Location": T._f(marker), "LocationName": "Hub",
                        "Tag": "ZoneInfo", "bDistanceFog": True},
              "rotation": [0, 0, 0], "scale": [1.0, 1.0, 1.0], "sky": False, "tag": "ZoneInfo", "zoneIndex": 1}]
    return {"lights": lights, "zones": zones, "look": {"glow_enabled": False, "glow_threshold": 255,
                                                         "glow_threshold_stored": False},
            "sky": None, "outdoor_sky": True, "lightmaps": []}


def build_mapinfo() -> dict:
    return {"MapName": "HUB", "LegendMap": "The OpenSCCT hub: meet, move and pick a match.",
            "LegendMapInBrief": "1. Central plaza||2. Training garden||3. Match district||4. Industrial range||5. Easy course||6. Waterfront lounge||7. Overgrown ruins||8. Frozen observatory",
            "ExpliMapInBriefSpy": "Walk, climb and meet other players.",
            "ExpliMapInBriefSentry": "Walk, climb and meet other players.", "modes": {}}


def areas_manifest(gameplay: dict, traversal: dict) -> dict:
    starts = [{"id": a["id"], "team": a["props"]["TeamNumber"], "location": [a["props"]["Location"][k] for k in "XYZ"],
               "yaw": a["props"].get("Rotation", {}).get("Yaw", 0)}
              for a in gameplay["actors"] if a["class"] == "Engine.PlayerStart"]
    return {"format": "openscct.hub/1", "map": L.MAP_ID, "layoutVersion": L.LAYOUT_VERSION,
            "generator": f"{GENERATOR} {GENERATOR_VERSION}", "units": "uu, UE space (Z up, yaw 16384 = +Y)",
            "hall": {"lo": list(L.HALL_LO), "hi": list(L.HALL_HI)},
            "teleports": L.TELEPORTS,
            "areas": [{"id": a.id, "kind": a.kind, "title": a.title, "lo": list(a.lo), "hi": list(a.hi), "phase": a.phase,
                       **a.data} for a in L.AREAS],
            "starts": starts, "ges": [{"id": g["id"], "kind": g["kind"], "points": g["points"]} for g in traversal["ges"]]}


def generate(out: Path) -> tuple[dict, dict]:
    polys = world_polys()
    faces = [T.Face(list(p.verts), p.material) for p in polys]
    gameplay = build_gameplay()
    traversal = build_traversal()
    movers = {"format": "svm.movers/1", "movers": [
        {"actor": a["id"], "keys": [{}, {"time": a["props"]["MoveTime"]}]}
        for a in gameplay["actors"] if a["class"] == "Engine.Mover" and a["props"].get("Tag") == "HubCourseMover"]}
    files = {"ui/mapinfo.json": canonical.dump_file(build_mapinfo(), ascii_keys=False),
             "sim/movers.json": canonical.dump_file(movers)}
    manifest = write_svmap(Path(out), map_id=L.MAP_ID, title=L.TITLE,
                           source={"kind": "synthetic", "generator": GENERATOR, "generatorVersion": GENERATOR_VERSION},
                           gameplay=gameplay, traversal=traversal, collision=build_collision(polys),
                           render=build_render(faces), requires=[], files=files, format_version=MAP_FORMAT_VERSION)
    return manifest, areas_manifest(gameplay, traversal)


def _tree(root: Path) -> dict[str, bytes]:
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in sorted(root.rglob("*")) if p.is_file()}


def check(out: Path) -> list[str]:
    with tempfile.TemporaryDirectory() as tmp:
        _, areas = generate(Path(tmp) / L.MAP_ID)
        fresh = _tree(Path(tmp) / L.MAP_ID)
    have = _tree(Path(out)) if Path(out).exists() else {}
    diffs = [f"differs: {k}" for k in sorted(fresh) if k in have and have[k] != fresh[k]]
    diffs += [f"missing: {k}" for k in sorted(fresh) if k not in have]
    diffs += [f"extra: {k}" for k in sorted(have) if k not in fresh]
    if not MANIFEST.is_file() or MANIFEST.read_bytes() != canonical.dump_file(areas):
        diffs.append(f"differs: {MANIFEST.relative_to(REPO).as_posix()}")
    return diffs


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(prog="python -B tools/hubmap/generate.py", description="Generate the Hub SVMap.")
    ap.add_argument("--out", type=Path, default=DEFAULT_OUT, help="map folder (default %(default)s)")
    ap.add_argument("--check", action="store_true", help="compare a fresh generation with --out and the manifest")
    ap.add_argument("--no-manifest", action="store_true", help="do not write fixtures/hubmap/areas.json")
    args = ap.parse_args(argv)
    if args.check:
        diffs = check(args.out)
        for line in diffs:
            print(line)
        print("up to date" if not diffs else f"{len(diffs)} difference(s)")
        return 1 if diffs else 0
    if args.out.exists() and any(args.out.iterdir()) and not (args.out / "manifest.json").exists():
        print(f"{args.out} is not empty and is not an SVMap folder; refusing to overwrite", file=sys.stderr)
        return 2
    manifest, areas = generate(args.out)
    if not args.no_manifest:
        MANIFEST.parent.mkdir(parents=True, exist_ok=True)
        MANIFEST.write_bytes(canonical.dump_file(areas))
    from svmbuild.validation import validate_svmap  # noqa: PLC0415

    errs = validate_svmap(args.out)
    for e in errs:
        print(e, file=sys.stderr)
    print(f"wrote {args.out} sim={manifest['hashes']['sim']} areas={len(areas['areas'])} starts={len(areas['starts'])}")
    return 1 if errs else 0


if __name__ == "__main__":
    raise SystemExit(main())
