"""render/ and ui/ of a converted map, built on scctpkg's glTF exporter (scctpkg/export_gltf.py).

The exporter writes <Map>.gltf + .bin, textures/*.png and scene.json. This module repacks that output:

- render/world.glb: the exporter's glTF as one binary file. Scene roots `World` (the BSP meshes, with lightmaps on
  TEXCOORD_1) and `Library` (one node per static mesh variant the map draws, named by asset path, plus projector
  decals and cloth patches). Node extras carry {"svmap": {"role", "asset", "variant"}}; a mesh that a map
  MeshEmitter draws also carries "particle": true (the client builds it for the emitter view even when a placed
  actor uses it too). The instance table (every placed mesh actor with its UE transform and draw flags) is in the
  document's extras.svmap.instances.
- render/textures/*.png: every texture and lightmap the glTF and the other files name (PNG for now).
- render/materials.json: per glTF material, the exporter's blend flags, its source material and the material's
  gameplay flags (SurfaceType, bClimbable, bNoSplitJump, bNoStickyCam, vision flags, OcclusionType); the material
  object graph (every Shader, FinalBlend, Combiner, modifier and texture reachable from a used material, with
  typed properties); and the exporter's additive, modulate, lightmapped and vertex-lit lists.
- render/lighting.json: lights (exporter fields plus the typed light properties of the actor), zones (fog,
  ambient, zone index), the sky zone, LevelInfo look settings and the lightmaps.
- render/fx.json: every Emitter actor with its sub-emitters taken from its own Emitters array (the exact owner;
  the exporter attributes sub-emitters to the nearest preceding Emitter export), typed; projectors, rain and cloth.
- render/projectors.json: every texture projector with its texture, frustum and blend ops (the client draws them as
  volumes over the depth buffer).
- render/lightbeams.json: the lights that draw a light beam, with their beam values and the beam slice meshes.
- ui/mapinfo.json: the SMapSettings texts (map name, briefings, per-mode texts).
"""

from __future__ import annotations

import json
import shutil
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from .. import canonical
from ..sources.typedprops import Ref, bag_to_json, default_resolver, walk_refs
from ..writers.glb import pack_glb
from .source import ActorInfo, MapSource


def _safe(name: str) -> str:
    return "".join(c if c.isalnum() or c in "_-" else "_" for c in name)


def _clean(v: Any) -> Any:
    """JSON from the exporter with non-finite floats as null (the canonical writer rejects them)."""
    if isinstance(v, float):
        return v if v == v and v not in (float("inf"), float("-inf")) else None
    if isinstance(v, dict):
        return {str(k): _clean(x) for k, x in v.items()}
    if isinstance(v, (list, tuple)):
        return [_clean(x) for x in v]
    return v


def _ascii_file_name(name: str) -> str:
    return "".join(c if c.isascii() else f"_u{ord(c):04x}" for c in name)


def _replace_strings(v: Any, table: Dict[str, str]) -> Any:
    if isinstance(v, str):
        return table.get(v, v)
    if isinstance(v, dict):
        return {k: _replace_strings(x, table) for k, x in v.items()}
    if isinstance(v, list):
        return [_replace_strings(x, table) for x in v]
    return v


def _ue_vector(v: Any) -> List[float]:
    return [float(v.get(k, 0.0)) for k in ("X", "Y", "Z")] if isinstance(v, dict) else [0.0, 0.0, 0.0]


def _ue_rotator(v: Any) -> List[int]:
    return [int(v.get(k, 0)) for k in ("Pitch", "Yaw", "Roll")] if isinstance(v, dict) else [0, 0, 0]


def _json_bytes(doc: Any) -> bytes:
    return canonical.dump_file(_clean(doc), ascii_keys=False)


class RenderBuilder:
    def __init__(self, src: MapSource, work: Path, gameplay_ids: set):
        self.src = src
        self.work = Path(work)
        self.gameplay_ids = gameplay_ids
        self.stats: Dict[str, Any] = {}
        self.files: Dict[str, bytes] = {}
        self._particle_refs: Optional[List[Ref]] = None
        self._spawn_refs: Optional[List[Tuple[Ref, Optional[str], dict]]] = None

    # ---- export ------------------------------------------------------------------------------

    def export(self) -> Tuple[dict, bytes, dict]:
        from ..paths import import_scctpkg  # noqa: PLC0415

        import_scctpkg(require_class_defaults=True)
        from scctpkg import export_gltf  # noqa: PLC0415

        export_gltf._EMITTER_PNGS.clear()   # per-process memo of written emitter PNGs; the work folder is new
        if self.work.exists():
            shutil.rmtree(self.work)
        self.work.mkdir(parents=True)
        extra = [r.ref for r in self._swap_meshes()] + [r.ref for r in self._particle_meshes()]
        st = export_gltf.export_map(self.src.path, self.src.pset, self.work, include_bsp=True,
                                    extra_mesh_refs=extra)
        out = self.work / self.src.name
        gltf = json.loads((out / f"{self.src.name}.gltf").read_text(encoding="utf-8"))
        binary = (out / f"{self.src.name}.bin").read_bytes()
        scene = json.loads((out / "scene.json").read_text(encoding="utf-8"))
        self.stats["exporter"] = {k: v for k, v in st.items() if k != "missing_meshes"}
        self.stats["missingMeshes"] = len(set(st.get("missing_meshes", [])))
        return gltf, binary, scene

    # ---- vision values -----------------------------------------------------------------------

    def _vision_values(self) -> Dict[str, Dict[str, Any]]:
        """Per actor that stores any of them: HeatIntensity, HeatOpacity, EMFIntensity, EMFOpacity and bEMFIsOn
        (stored, else the class default). Thermal and EMF vision draw meshes by these values."""
        names = (("HeatIntensity", "heat", 0.0), ("HeatOpacity", "heatOpacity", 1.0), ("EMFIntensity", "emf", 0.0),
                 ("EMFOpacity", "emfOpacity", 1.0), ("bEMFIsOn", "emfOn", True))
        out: Dict[str, Dict[str, Any]] = {}
        for info in self.src.actors:
            if not any(n in info.props for n, _, _ in names):
                continue
            values: Dict[str, Any] = {}
            for prop, key, fallback in names:
                v = info.props.get(prop)
                if v is None:
                    v = self.src.db.default(info.cls, prop)
                if v is None:
                    v = fallback
                values[key] = bool(v) if isinstance(fallback, bool) else float(v)
            out[info.name] = values
        return out

    # ---- names -------------------------------------------------------------------------------

    def _swap_meshes(self) -> List[Ref]:
        """The static meshes gameplay actors can change to at run time: SGamePlayObject TriggeredMesh and
        DamagedMeshes[].StaticMesh (a broken extinguisher, a light switched off, the SteeE crane wall after the blast).
        The library gets a node for each, so the client can draw the swap."""
        out: List[Ref] = []
        for info in self.src.actors:
            if info.category != "gameplay":
                continue
            refs = [info.props.get("TriggeredMesh")]
            damaged = info.props.get("DamagedMeshes")
            if isinstance(damaged, list):
                refs.extend(d.get("StaticMesh") for d in damaged if isinstance(d, dict))
            for r in refs:
                if isinstance(r, Ref) and r.ref != 0 and r.path and not r.is_class:
                    out.append(r)
        return out

    def _emitter_subs(self, info: ActorInfo) -> List[Tuple[Ref, Optional[str], dict]]:
        """The sub-emitters of an Emitter actor (its own Emitters array): (object ref, class, decoded properties)."""
        out = []
        for r in self.src.value(info, "Emitters") or []:
            if not isinstance(r, Ref) or r.ref == 0:
                continue
            cls, decoded, _refs, _pkg = self._decode_object(r.path)
            out.append((r, cls, decoded))
        return out

    def _spawn_particles(self) -> List[Tuple[Ref, Optional[str], dict]]:
        """The sub-emitters gameplay objects carry for their damage effects (SGamePlayObject SpawnableObjects[].
        SpawnParticle, inserted into an empty SSpawnableEmitter at run time): (object ref, class, decoded properties),
        each object once."""
        if self._spawn_refs is not None:
            return self._spawn_refs
        out: List[Tuple[Ref, Optional[str], dict]] = []
        seen = set()
        for info in self.src.actors:
            if info.category != "gameplay":
                continue
            entries = self.src.value(info, "SpawnableObjects")
            if not isinstance(entries, list):
                continue
            for entry in entries:
                r = entry.get("SpawnParticle") if isinstance(entry, dict) else None
                if not isinstance(r, Ref) or r.ref == 0 or not r.path or r.is_class or r.path in seen:
                    continue
                seen.add(r.path)
                cls, decoded, _refs, _pkg = self._decode_object(r.path)
                out.append((r, cls, decoded))
        self._spawn_refs = out
        return out

    def _emitters(self) -> List[ActorInfo]:
        return [info for info in self.src.actors
                if self.src.db.is_a(info.cls, "Engine.Emitter") and info.category not in ("editor", "skipped")]

    def _particle_meshes(self) -> List[Ref]:
        """The static meshes the map's MeshEmitters draw (Fact parts, ORP lamps, Aquarium vase shards and fish). The
        library gets a node for each, so the client can draw the particles with the mesh's own materials."""
        if self._particle_refs is not None:
            return self._particle_refs
        out: List[Ref] = []
        subs = [s for info in self._emitters() for s in self._emitter_subs(info)] + self._spawn_particles()
        for _r, cls, decoded in subs:
            if not cls or not self.src.db.is_a(cls, "Engine.MeshEmitter"):
                continue
            sm = decoded.get("StaticMesh")
            if isinstance(sm, Ref) and sm.ref != 0 and sm.path and not sm.is_class and self.src.own(sm):
                out.append(sm)
        self._particle_refs = out
        return out

    def _mesh_assets(self) -> Dict[str, str]:
        """scctpkg base node name (_safe of Package.Name) -> full asset path, from the actors' StaticMesh refs and
        the swap meshes."""
        out: Dict[str, str] = {}
        refs: List[Ref] = []
        for info in self.src.actors:
            sm = info.props.get("StaticMesh")
            if not isinstance(sm, Ref) or sm.ref == 0:
                sm = self.src.db.default(info.cls, "StaticMesh")
            refs.append(sm)
        refs.extend(self._swap_meshes())
        refs.extend(self._particle_meshes())
        for sm in refs:
            if not isinstance(sm, Ref) or sm.ref == 0 or not sm.path:
                continue
            parts = sm.path.split(".")
            short = f"{parts[0]}.{parts[-1]}"
            out.setdefault(_safe(short), sm.path)
            # Meshes of one name in several groups are keyed by their full path (scctpkg level._split_group_clashes).
            out.setdefault(_safe(sm.path), sm.path)
        return out

    def _material_root(self, key: str) -> Optional[str]:
        """Full path of the material behind a glTF material name (Package.Name plus exporter suffixes)."""
        base = key[5:] if key.startswith("PROJ.") else key
        base = base.split("__", 1)[0]
        if "." not in base or base.startswith("BSP.Default"):
            return None
        pkg_name, name = base.split(".", 1)
        pkg = self.src.pset.get(pkg_name)
        if pkg is None:
            return None
        e = pkg.find_export(name.split(".")[-1])
        if e is None:
            return None
        return self.src.db.cd.export_path(pkg, e)

    def _softbody(self, name: str, body: dict) -> dict:
        """A soft body's simulation settings for the client (scctpkg level.softbody_record) with its collSound and
        shotSound (stored, else the class default: Sound_Persos.divers.RA_SoftBo_perso, RA_SoftBo_shot)."""
        out = {k: v for k, v in body.items() if k not in ("ambient_glow", "unlit")}
        info = next((a for a in self.src.actors if a.name == name), None)
        for prop, key in (("collSound", "coll_sound"), ("shotSound", "shot_sound")):
            ref = self.src.value(info, prop) if info is not None else None
            out[key] = ref.path if isinstance(ref, Ref) and ref.ref != 0 and ref.path else ""
        return out

    # ---- world.glb ---------------------------------------------------------------------------

    def build_glb(self, gltf: dict, binary: bytes, scene: dict) -> Tuple[bytes, Dict[str, str]]:
        assets = self._mesh_assets()
        particle = {r.path for r in self._particle_meshes()}
        rename: Dict[str, str] = {}
        world_children: List[int] = []
        library_children: List[int] = []
        bases = sorted(assets, key=len, reverse=True)
        for i, node in enumerate(gltf["nodes"]):
            kind = (node.get("extras") or {}).get("scct_kind")
            name = node.get("name", "")
            if kind == "bsp":
                node["extras"] = {"svmap": {"role": "bsp", "sky": name.endswith("_Sky")}}
                world_children.append(i)
                continue
            asset, variant = None, ""
            for b in bases:
                if name == b or name.startswith(b + "__") or name.startswith(b + "_sk"):
                    asset, variant = assets[b], name[len(b):].lstrip("_")
                    break
            if asset is None:
                asset = f"{self.src.name}.{name}"
            new = asset + (f"#{variant}" if variant else "")
            rename[name] = new
            node["name"] = new
            node["extras"] = {"svmap": {"role": "libraryMesh", "asset": asset, "variant": variant}}
            if not variant and asset in particle:
                node["extras"]["svmap"]["particle"] = True
            library_children.append(i)
        n = len(gltf["nodes"])
        gltf["nodes"].append({"name": "World", "children": world_children, "extras": {"svmap": {"role": "world"}}})
        gltf["nodes"].append({"name": "Library", "children": library_children,
                              "extras": {"svmap": {"role": "library"}}})
        gltf["scenes"] = [{"name": self.src.name, "nodes": [n, n + 1]}]
        gltf["scene"] = 0
        gltf["asset"] = {"version": "2.0", "generator": "svmbuild (scctpkg export_gltf)"}
        instances = []
        vision = self._vision_values()
        for a in scene["actors"]:
            if a.get("kind") not in ("staticmesh", "mover") or not a.get("mesh") or not a.get("mesh_available"):
                continue
            if a.get("kind") == "bsp":
                continue
            entry = {"actor": a["name"], "class": a["class"], "kind": a["kind"], "mesh": rename.get(a["mesh"], a["mesh"]),
                     "location": a["location"], "rotation": {"Pitch": a["rotation"][0], "Yaw": a["rotation"][1],
                                                             "Roll": a["rotation"][2]},
                     "scale": a["scale"], "hidden": bool(a.get("hidden")), "gameplay": a["name"] in self.gameplay_ids}
            for k_src, k_dst in (("prepivot", "prePivot"), ("vertex_lit", "vertexLit"), ("authored_vc", "authoredColors"),
                                 ("vc_max", "bakedColorMax"), ("unlit", "unlit"), ("ambient_glow", "ambientGlow"),
                                 ("cull_distance", "cullDistance"), ("sky_zone", "skyZone"), ("skins", "skins"),
                                 ("projector_of", "projectorOf"), ("keypos", "keyPos"), ("basepos", "basePos")):
                if k_src in a:
                    entry[k_dst] = a[k_src]
            if a["name"] in vision:
                entry["vision"] = vision[a["name"]]
            if "softbody" in a:
                entry["softBody"] = self._softbody(a["name"], a["softbody"])
            instances.append(entry)
        gltf["extras"] = {"svmap": {"map": self.src.name, "instances": instances}}
        self.stats["instances"] = len(instances)
        self.stats["libraryMeshes"] = len(library_children)
        self.stats["meshes"] = len(gltf.get("meshes", []))
        self.stats["materials"] = len(gltf.get("materials", []))
        tris = 0
        for m in gltf.get("meshes", []):
            for p in m.get("primitives", []):
                tris += gltf["accessors"][p["indices"]]["count"] // 3
        self.stats["renderTriangles"] = tris
        return pack_glb(gltf, binary), rename

    # ---- materials.json ----------------------------------------------------------------------

    def _decode_object(self, path: str) -> Tuple[Optional[str], dict, List[Ref], Any]:
        pkg, e = self.src.find_path(path)
        if pkg is None or e is None or e.size <= 0:
            return None, {}, [], None
        cls = self.src.db.find(f"{e.class_package}.{e.class_name}")
        rec = self.src.db.record(cls) if cls else None
        if rec is None:
            return cls, {}, [], pkg
        from scctpkg.properties import skip_state_frame  # noqa: PLC0415
        try:
            r = pkg.export_reader(e)
            skip_state_frame(r, e.flags)
            decoded = self.src.db.decoder.tagged(r, pkg, rec["_field"])
        except Exception as ex:  # noqa: BLE001
            self.src.notes.add("material-decode", f"{path}: {ex}")
            return cls, {}, [], pkg
        return cls, decoded, list(walk_refs(decoded)), pkg

    def build_materials(self, gltf: dict, scene: dict, rename: Dict[str, str]) -> dict:
        db = self.src.db
        graph: Dict[str, dict] = {}
        gameplay: Dict[str, dict] = {}

        def visit(path: str, depth: int = 0) -> None:
            if path in graph or depth > 16:
                return
            cls, decoded, refs, pkg = self._decode_object(path)
            if cls is None or not db.is_a(cls, "Engine.Material"):
                return
            graph[path] = {"class": cls, "props": bag_to_json(decoded, default_resolver, self.src.notes, path)}
            for r in refs:
                if r.ref == 0 or not r.path or r.is_class:
                    continue
                visit(r.path, depth + 1)

        flags = scene.get("material_flags", {})
        materials: Dict[str, dict] = {}
        for i, m in enumerate(gltf.get("materials", [])):
            key = m.get("name", f"material{i}")
            root = self._material_root(key)
            entry: dict = {"index": i, "flags": flags.get(_safe(key), {})}
            if root:
                entry["source"] = root
                visit(root)
                if root in graph and root not in gameplay:
                    props = graph[root]["props"]
                    cls = graph[root]["class"]
                    g = {}
                    for name in ("SurfaceType", "bClimbable", "bNoSplitJump", "bNoStickyCam", "OcclusionType",
                                 "bHideInEMFVision", "bHideInThermalVision", "bEchelonLightThrough"):
                        v = props.get(name, db.default(cls, name))
                        if v is not None:
                            g[name] = v if not isinstance(v, Ref) else None
                    gameplay[root] = g
            materials[key] = entry

        def remap(rows: list) -> list:
            out = []
            for r in rows:
                r = dict(r)
                for k in ("name", "base"):
                    if k in r and r[k] in rename:
                        r[k] = rename[r[k]]
                out.append(r)
            return out

        doc = {"materials": materials, "graph": graph, "gameplay": gameplay,
               "additive": scene.get("additive_materials", []), "modulate": scene.get("modulate_materials", []),
               "lightmapped": scene.get("lightmapped_materials", []),
               "vertexLitMeshes": remap(scene.get("vertex_lit_meshes", [])),
               "authoredColorMeshes": [rename.get(n, n) for n in scene.get("authored_vc_meshes", [])]}
        self.stats["materialGraph"] = len(graph)
        return doc

    # ---- lighting.json and fx.json -----------------------------------------------------------

    def _typed(self, info: ActorInfo) -> dict:
        return bag_to_json(info.props, self.src.resolve, self.src.notes, info.name)

    def build_lighting(self, scene: dict, zone_index: Dict[str, int], lightmaps: List[str]) -> dict:
        by_name = {a.name: a for a in self.src.actors}
        lights, zones = [], []
        for a in scene["actors"]:
            info = by_name.get(a["name"])
            if a["kind"] == "light":
                entry = dict(a)
                entry["gameplay"] = a["name"] in self.gameplay_ids
                if info is not None:
                    entry["props"] = self._typed(info)
                lights.append(entry)
            elif a["kind"] == "zone":
                entry = dict(a)
                entry["zoneIndex"] = zone_index.get(a["name"])
                if info is not None:
                    entry["props"] = self._typed(info)
                zones.append(entry)
        self.stats["lights"] = len(lights)
        return {"lights": lights, "zones": zones, "sky": scene.get("sky"), "look": scene.get("look"),
                "lightmaps": lightmaps}

    def _sub_json(self, r: Ref, cls: Optional[str], decoded: dict, textures: set) -> dict:
        props = bag_to_json(decoded, default_resolver, self.src.notes, r.path)
        sub = {"object": r.path, "class": cls, "props": props}
        tex = props.get("Texture")
        if isinstance(tex, dict) and "$asset" in tex:
            parts = tex["$asset"].split(".")
            png = f"textures/{_safe(parts[0] + '.' + parts[-1])}.png"
            if png in textures:
                sub["texture"] = png
        return sub

    def build_fx(self, scene: dict, textures: set) -> dict:
        emitters = {}
        for info in self._emitters():
            subs = [self._sub_json(r, cls, decoded, textures) for r, cls, decoded in self._emitter_subs(info)]
            emitters[info.name] = {"class": info.cls, "gameplay": info.name in self.gameplay_ids,
                                   "props": self._typed(info), "emitters": subs}
        # The gameplay objects' damage effects, by object path (the simulation names them by it).
        spawn = {r.path: self._sub_json(r, cls, decoded, textures) for r, cls, decoded in self._spawn_particles()}
        extra = {"projectors": [], "rain": [], "patches": []}
        for a in scene["actors"]:
            if a.get("projector_of"):
                extra["projectors"].append(a)
            elif "rain" in a:
                extra["rain"].append(a)
            elif str(a.get("mesh", "")).startswith("PATCH_"):
                extra["patches"].append(a)
        self.stats["emitters"] = len(emitters)
        self.stats["subEmitters"] = sum(len(e["emitters"]) for e in emitters.values())
        self.stats["particleMeshes"] = len({r.path for r in self._particle_meshes()})
        self.stats["spawnParticles"] = len(spawn)
        return {"emitters": emitters, "spawnParticles": spawn, **extra}

    def write_spawn_particle_textures(self, tex_dir: Path) -> None:
        """The PNGs of the gameplay objects' damage effect textures, named as the exporter names the placed emitters'
        (textures/<Package.Name>.png), for those it did not write."""
        from scctpkg import export_gltf  # noqa: PLC0415
        from scctpkg.texture import read_texture  # noqa: PLC0415

        for _r, _cls, decoded in self._spawn_particles():
            tex = decoded.get("Texture")
            if not isinstance(tex, Ref) or tex.ref == 0 or not tex.path or tex.is_class:
                continue
            parts = tex.path.split(".")
            path = tex_dir / f"{_safe(parts[0] + '.' + parts[-1])}.png"
            if path.is_file():
                continue
            pkg, e = self.src.ref_export(tex)
            if pkg is None or e is None or e.class_name != "Texture":
                self.src.notes.add("spawn-particle-texture", tex.path)
                continue
            tx = read_texture(pkg, e)
            if tx is None or tx.image is None:
                self.src.notes.add("spawn-particle-texture", tex.path)
                continue
            tex_dir.mkdir(parents=True, exist_ok=True)
            export_gltf._write_png(tx.image, path)

    # ---- projectors.json ---------------------------------------------------------------------

    def build_projectors(self, scene: dict, tex_dir: Path) -> dict:
        """Every texture projector (Projector, DynamicProjector) as the client draws it, a volume over whatever lies in
        its frustum: placement, FOV, MaxTraceDistance, the blend ops, the flags, its texture PNG (written into
        `tex_dir`), the texture size, its animation (TexPanner and friends, AnimNext flipbook)."""
        from scctpkg import export_gltf  # noqa: PLC0415
        from scctpkg.materials import MaterialResolver  # noqa: PLC0415

        by_name = {a.name: a for a in self.src.actors}
        resolver = MaterialResolver(self.src.pset)
        tex_uri: Dict[str, Optional[str]] = {}
        out = []
        for a in scene["actors"]:
            if a.get("kind") != "projector":
                continue
            info = by_name.get(a["name"])
            ref = info.props.get("ProjTexture") if info is not None else None
            if not isinstance(ref, Ref) or ref.ref == 0 or not self.src.own(ref):
                continue
            res = resolver.resolve(self.src.pkg, ref.ref)
            if res.skip or res.texture is None:
                self.src.notes.add("projector-texture", f"{a['name']}: {ref.path}")
                continue
            uri = export_gltf._ensure_texture(res, tex_dir, tex_uri)
            if uri is None:
                continue
            tex = res.texture
            entry = {"name": a["name"], "class": a["class"], "location": a["location"], "rotation": a["rotation"],
                     "scale": a["scale"], "texture": uri,
                     "size": [float(tex.props.get("USize") or tex.width or 256), float(tex.props.get("VSize") or tex.height or 256)],
                     "fov": a.get("fov", 0.0), "maxTrace": a.get("max_trace", 1000.0), "fbBlend": a.get("fb_blend", 1),
                     "matBlend": a.get("mat_blend", 0), "projectBsp": a.get("project_bsp", True),
                     "projectStaticMesh": a.get("project_static_mesh", True), "backfaces": a.get("backfaces", False),
                     "gradient": a.get("gradient", False),
                     "projectActor": bool(self.src.value(info, "bProjectActor", True))}
            if res.uv_anim:
                entry["uvAnim"] = res.uv_anim
            if res.uv_matrix:
                # A static TexScaler / TexRotator over the texture (AquaE's rain: Puie.Rain, UScale 0.1, tiles the
                # drops 10 times over the projector), as the exporter's [a, b, c, d, e, f].
                entry["uvMatrix"] = [float(v) for v in res.uv_matrix]
            if res.flipbook:
                fb = export_gltf._flipbook_png(res.key, res.flipbook, tex_dir, tex_uri)
                if fb:
                    entry["flipbook"] = fb
            out.append(entry)
        self.stats["projectors"] = len(out)
        return {"projectors": out}

    # ---- lightbeams.json ---------------------------------------------------------------------

    def build_light_beams(self, scene: dict) -> dict:
        """Lights that draw a light beam (UsesLightBeam with a LightBeamMesh, docs/RENDERING.md#light-beams): the beam
        values of each (stored, else the class default) and the slice meshes they name, as plain positions and
        triangle indices in the mesh's own space (x 0..1 along the beam, y and z -1..1 across it)."""
        from scctpkg.staticmesh import read_static_mesh  # noqa: PLC0415

        placed = {a["name"]: a for a in scene["actors"]}
        meshes: Dict[str, Optional[dict]] = {}
        beams = []
        for info in self.src.actors:
            if info.category in ("editor", "skipped"):
                continue
            mesh = self.src.value(info, "LightBeamMesh")
            if not self.src.value(info, "UsesLightBeam") or not isinstance(mesh, Ref) or mesh.ref == 0 or not mesh.path:
                continue
            if mesh.path not in meshes:
                meshes[mesh.path] = None
                pkg, e = self.src.ref_export(mesh)
                if pkg is not None and e is not None and e.class_name == "StaticMesh":
                    sm = read_static_mesh(pkg, e)
                    meshes[mesh.path] = {"positions": [round(float(v), 5) for v in sm.positions.reshape(-1)],
                                         "indices": [int(i) for i in sm.indices]}
            if meshes[mesh.path] is None:
                self.src.notes.add("light-beam-mesh", f"{info.name}: {mesh.path}")
                continue
            a = placed.get(info.name) or {"location": _ue_vector(info.props.get("Location")),
                                          "rotation": _ue_rotator(info.props.get("Rotation"))}
            num = lambda k, d: float(self.src.value(info, k, d) or 0.0)  # noqa: E731
            light_type = self.src.value(info, "LightType", 1)
            beams.append({
                "name": info.name, "class": info.cls.split(".")[-1], "mesh": mesh.path,
                "location": a.get("location"), "rotation": a.get("rotation"),
                "echelonRange": num("EchelonRange", 0.0), "spotWidth": num("SpotWidth", 0.0),
                "spotHeight": num("SpotHeight", 0.0), "nearPlaneRatio": num("NearPlaneRatio", 0.0),
                "falloffScale": num("LightBeamFalloffScale", 1.0), "alpha": int(num("LightBeamInitialAlpha", 128.0)),
                "brightness": num("LightBrightness", 64.0), "hue": int(num("LightHue", 0.0)),
                "saturation": int(num("LightSaturation", 255.0)), "color": a.get("color"),
                "lightOn": int(light_type or 0) != 0, "initiallyOn": bool(self.src.value(info, "bInitialyOn", True))
                if info.cls.endswith("STriggerLight") else True,
                "gameplay": info.name in self.gameplay_ids})
        self.stats["lightBeams"] = len(beams)
        return {"meshes": {k: v for k, v in meshes.items() if v is not None}, "beams": beams}

    # ---- all ---------------------------------------------------------------------------------

    def build(self, zone_index: Dict[str, int], ui: dict) -> Dict[str, bytes]:
        gltf, binary, scene = self.export()
        tex_dir = self.work / self.src.name / "textures"
        projectors = self.build_projectors(scene, tex_dir)   # writes its PNGs before the textures are collected
        self.write_spawn_particle_textures(tex_dir)
        textures = {}
        renamed: Dict[str, str] = {}
        if tex_dir.is_dir():
            for p in sorted(tex_dir.iterdir()):
                if p.is_file():
                    name = _ascii_file_name(p.name)
                    if name != p.name:
                        renamed[f"textures/{p.name}"] = f"textures/{name}"
                    textures[f"textures/{name}"] = p.read_bytes()
        if renamed:
            # Manifest paths are ASCII: texture names with accents (PCF09_TXT.beton_abimé3) get escapes.
            gltf, scene = _replace_strings(gltf, renamed), _replace_strings(scene, renamed)
            self.stats["renamedTextures"] = len(renamed)
        glb, rename = self.build_glb(gltf, binary, scene)
        files = {"world.glb": glb}
        files.update(textures)
        lightmaps = sorted(k for k in textures if "_lightmap" in k)
        files["materials.json"] = _json_bytes(self.build_materials(gltf, scene, rename))
        files["lighting.json"] = _json_bytes(self.build_lighting(scene, zone_index, lightmaps))
        files["fx.json"] = _json_bytes(self.build_fx(scene, set(textures)))
        files["projectors.json"] = _json_bytes(_replace_strings(projectors, renamed))
        files["lightbeams.json"] = _json_bytes(self.build_light_beams(scene))
        self.stats["textures"] = len(textures)
        self.ui = ui
        shutil.rmtree(self.work, ignore_errors=True)
        return files
