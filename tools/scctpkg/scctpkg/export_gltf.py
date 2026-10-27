"""Export one SCCT map to glTF 2.0 + PNG textures + scene.json.

Output layout (out/<Map>/):
    <Map>.gltf, <Map>.bin      world BSP (one mesh, one primitive per material)
                               plus every referenced static mesh as its own node
    textures/<Package>_<Name>.png
    scene.json                 actor list consumed by ue5/Scripts/import_scct_map.py

Coordinate conversion: SCCT/UE2 is left-handed Z-up (X forward, Y right,
Z up). glTF is right-handed Y-up. We map (x, y, z) -> (x, z, -y) and reverse
triangle winding, which the UE5 Interchange importer maps back to UE space
exactly. Units are centimetres in both engines.
"""

from __future__ import annotations

import json
import math
import struct
from collections import defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional, Tuple

import numpy as np

from .bsp import BspPolygon, extract_polygons, point_zone, read_model, PF_MIRRORED, PF_TWO_SIDED, PF_UNLIT
from .bsplight import read_bsp_lighting, lightmap_image, BspLighting
import hashlib
from .level import ActorRecord, extract_level, extract_look
from .materials import MaterialResolver, ResolvedMaterial
from .package import Package
from .packageset import PackageSet
from .staticmesh import StaticMesh, read_static_mesh


def _safe(name: str) -> str:
    return "".join(c if c.isalnum() or c in "_-" else "_" for c in name)


# UE5 Interchange maps glTF (x, y, z) to UE (x, z, y) and multiplies by 100
# (glTF units are metres); it reverses triangle winding itself for that
# reflection. So we write UE (x, y, z) as glTF (x/100, z/100, y/100) and keep
# the original winding.
UE_TO_GLTF_SCALE = 0.01


def _to_gltf(v: np.ndarray) -> np.ndarray:
    out = np.empty_like(v, dtype=np.float32)
    out[:, 0] = v[:, 0] * UE_TO_GLTF_SCALE
    out[:, 1] = v[:, 2] * UE_TO_GLTF_SCALE
    out[:, 2] = v[:, 1] * UE_TO_GLTF_SCALE
    return out


def _dir_to_gltf(v: np.ndarray) -> np.ndarray:
    out = np.empty_like(v, dtype=np.float32)
    out[:, 0] = v[:, 0]
    out[:, 1] = v[:, 2]
    out[:, 2] = v[:, 1]
    return out


@dataclass
class _Prim:
    positions: np.ndarray
    normals: np.ndarray
    uvs: np.ndarray
    indices: np.ndarray
    material_key: str


@dataclass
class _MeshOut:
    name: str
    prims: List[_Prim]


class GltfWriter:
    def __init__(self):
        self.material_flags: Dict[str, dict] = {}
        self.bin = bytearray()
        self.buffer_views: List[dict] = []
        self.accessors: List[dict] = []
        self.meshes: List[dict] = []
        self.nodes: List[dict] = []
        self.materials: List[dict] = []
        self.images: List[dict] = []
        self.textures: List[dict] = []
        self.samplers = [{"magFilter": 9729, "minFilter": 9987, "wrapS": 10497, "wrapT": 10497}]
        self.material_index: Dict[str, int] = {}
        self.texture_index: Dict[str, int] = {}

    def texture(self, uri: str) -> int:
        """One glTF image + texture per PNG: repeated URIs made Interchange import numbered duplicates
        (Bunker_SolGravierB2561, ..._PTH1) and the importer's texture lookup then picked an arbitrary one."""
        if uri not in self.texture_index:
            self.images.append({"uri": uri})
            self.textures.append({"sampler": 0, "source": len(self.images) - 1})
            self.texture_index[uri] = len(self.textures) - 1
        return self.texture_index[uri]

    def _add_view(self, data: bytes, target: Optional[int]) -> int:
        while len(self.bin) % 4:
            self.bin.append(0)
        off = len(self.bin)
        self.bin += data
        view = {"buffer": 0, "byteOffset": off, "byteLength": len(data)}
        if target:
            view["target"] = target
        self.buffer_views.append(view)
        return len(self.buffer_views) - 1

    def add_vec_accessor(self, arr: np.ndarray, comp: int, vtype: str, target=34962, minmax=False) -> int:
        arr = np.ascontiguousarray(arr, dtype=np.float32)
        idx = self._add_view(arr.tobytes(), target)
        acc = {"bufferView": idx, "componentType": 5126, "count": int(arr.shape[0]), "type": vtype}
        if minmax:
            mn = arr.min(axis=0)
            mx = arr.max(axis=0)
            acc["min"] = [float(x) for x in np.atleast_1d(mn)]
            acc["max"] = [float(x) for x in np.atleast_1d(mx)]
        self.accessors.append(acc)
        return len(self.accessors) - 1

    def add_index_accessor(self, idx_arr: np.ndarray) -> int:
        arr = np.ascontiguousarray(idx_arr, dtype=np.uint32)
        idx = self._add_view(arr.tobytes(), 34963)
        self.accessors.append({"bufferView": idx, "componentType": 5125, "count": int(arr.shape[0]), "type": "SCALAR"})
        return len(self.accessors) - 1

    def material(self, key: str, resolved: Optional[ResolvedMaterial], texture_uri: Optional[str],
                 lightmap_uri: Optional[str] = None) -> int:
        if key in self.material_index:
            return self.material_index[key]
        mat = {"name": key, "pbrMetallicRoughness": {"metallicFactor": 0.0, "roughnessFactor": 0.85}}
        if lightmap_uri:
            # Baked UE2 lightmap on TEXCOORD_1: carried as the glTF occlusion texture so the importer gets the
            # texture asset; the import script builds the real (diffuse x lightmap) material from scene.json.
            mat["occlusionTexture"] = {"index": self.texture(lightmap_uri), "texCoord": 1}
            mat["extras"] = {"scct_lightmap": lightmap_uri}
        tex_index = None
        if texture_uri:
            tex_index = self.texture(texture_uri)
            mat["pbrMetallicRoughness"]["baseColorTexture"] = {"index": tex_index}
        else:
            lower = key.lower()
            if resolved is not None and (resolved.translucent or any(w in lower for w in ("vitre", "glass", "verre", "window", "fenetre"))):
                # Glass shaders without a resolvable diffuse: pale translucent pane instead of an opaque grey slab.
                mat["pbrMetallicRoughness"]["baseColorFactor"] = [0.7, 0.85, 0.9, 0.25]
                mat["pbrMetallicRoughness"]["roughnessFactor"] = 0.1
                mat["alphaMode"] = "BLEND"
                mat["doubleSided"] = True
            elif resolved is not None and resolved.color is not None:
                r, g, b, _ = resolved.color
                # glTF factors are linear; UE2 colours are display (sRGB) values.
                lin = lambda c: (c / 255.0) / 12.92 if c / 255.0 <= 0.04045 else ((c / 255.0 + 0.055) / 1.055) ** 2.4
                mat["pbrMetallicRoughness"]["baseColorFactor"] = [lin(r), lin(g), lin(b), 1.0]
            else:
                mat["pbrMetallicRoughness"]["baseColorFactor"] = [0.6, 0.6, 0.6, 1.0]
        if resolved is not None:
            # Env-mapped shaders are not made glossy here: meshes that keep their Interchange material (runtime
            # props, shared packages) cannot follow the fidelity profile, and a fixed gloss leaked into Faithful. The
            # importer's own instances carry Roughness + SpecMask + EnvColor behind the profile switch.
            if resolved.two_sided:
                mat["doubleSided"] = True
            if resolved.modulate:
                # Multiplicative (modulate 2x / darken): the importer swaps in a Modulate-blend material.
                mat["alphaMode"] = "BLEND"
                mat["doubleSided"] = True
                mat["extras"] = {"scct_blend": "modulate", "scct_modulate": resolved.modulate}
                if tex_index is not None:
                    mat["emissiveTexture"] = {"index": tex_index}
                    mat["emissiveFactor"] = [1.0, 1.0, 1.0]
            elif resolved.additive:
                # Unlit additive (light beams, caustic sheets, water surfaces): the importer swaps the
                # generated material for an additive emissive one; glTF itself only gets a hint.
                mat["alphaMode"] = "BLEND"
                mat["doubleSided"] = True
                mat["extras"] = {"scct_blend": "additive"}
                if resolved.additive_gain != 1.0 and not key.endswith("__unlit"):
                    mat["extras"]["scct_add_gain"] = resolved.additive_gain
                if resolved.flipbook and getattr(self, "tex_dir", None) is not None:
                    # Animated texture (AnimNext chain): all frames in one atlas for the importer's flipbook UVs.
                    fb = _flipbook_png(key, resolved.flipbook, self.tex_dir, self.tex_uri)
                    if fb:
                        mat["extras"]["scct_flipbook"] = fb
                if tex_index is not None:
                    mat["emissiveTexture"] = {"index": tex_index}
                    mat["emissiveFactor"] = [1.0, 1.0, 1.0]
            elif resolved.translucent:
                mat["alphaMode"] = "BLEND"
            elif resolved.masked:
                mat["alphaMode"] = "MASK"
                mat["alphaCutoff"] = 0.5
        self.materials.append(mat)
        self.material_index[key] = len(self.materials) - 1
        if resolved is not None:
            flags = {"masked": bool(resolved.masked and not resolved.translucent and not resolved.additive
                                    and not resolved.modulate),
                     "two_sided": bool(resolved.two_sided), "translucent": bool(resolved.translucent),
                     "unlit": bool(getattr(resolved, "unlit", False))}
            if texture_uri:
                # The importer falls back to this when an Interchange instance lacks its base colour texture.
                flags["texture"] = texture_uri
            if resolved.uv_anim:
                flags["uv_anim"] = resolved.uv_anim
            comb = _combine_flags(key, resolved, getattr(self, "tex_dir", None), getattr(self, "tex_uri", None))
            if comb:
                flags["combine"] = comb
            if getattr(resolved, "env_map", False):
                flags["env_map"] = True
                if getattr(resolved, "env_color", None) is not None:
                    flags["env_color"] = [round(c, 4) for c in resolved.env_color]
            self.material_flags[key] = flags
        return self.material_index[key]

    def add_color_accessor(self, rgba_u8: np.ndarray) -> int:
        arr = np.ascontiguousarray(rgba_u8, dtype=np.uint8)
        idx = self._add_view(arr.tobytes(), 34962)
        self.accessors.append({"bufferView": idx, "componentType": 5121, "normalized": True, "count": int(arr.shape[0]), "type": "VEC4"})
        return len(self.accessors) - 1

    def mesh(self, name: str, prims: List[tuple]) -> int:
        """prims: (pos, nrm, uv, idx, mat[, uv2[, color_rgba_u8]])."""
        gprims = []
        for prim in prims:
            pos, nrm, uv, idx, mat = prim[:5]
            uv2 = prim[5] if len(prim) > 5 else None
            col = prim[6] if len(prim) > 6 else None
            if len(idx) == 0:
                continue
            attrs = {"POSITION": self.add_vec_accessor(_to_gltf(pos), 3, "VEC3", minmax=True)}
            if uv2 is not None and len(uv2) == len(pos):
                attrs["TEXCOORD_1"] = self.add_vec_accessor(uv2, 2, "VEC2")
            if col is not None and len(col) == len(pos):
                attrs["COLOR_0"] = self.add_color_accessor(col)
            if nrm is not None and len(nrm) == len(pos):
                n = _dir_to_gltf(nrm)
                lens = np.linalg.norm(n, axis=1, keepdims=True)
                lens[lens == 0] = 1
                attrs["NORMAL"] = self.add_vec_accessor(n / lens, 3, "VEC3")
            if uv is not None and len(uv) == len(pos):
                attrs["TEXCOORD_0"] = self.add_vec_accessor(uv, 2, "VEC2")
            gprims.append({"attributes": attrs, "indices": self.add_index_accessor(idx), "material": mat, "mode": 4})
        self.meshes.append({"name": name, "primitives": gprims})
        return len(self.meshes) - 1

    def node(self, name: str, mesh: int, translation=None, extras=None) -> int:
        n = {"name": name, "mesh": mesh}
        if translation is not None:
            n["translation"] = translation
        if extras:
            n["extras"] = extras
        self.nodes.append(n)
        return len(self.nodes) - 1

    def write(self, path: Path) -> None:
        bin_path = path.with_suffix(".bin")
        bin_path.write_bytes(bytes(self.bin))
        doc = {
            "asset": {"version": "2.0", "generator": "scctpkg"},
            "scene": 0,
            "scenes": [{"nodes": list(range(len(self.nodes)))}],
            "nodes": self.nodes,
            "meshes": self.meshes,
            "materials": self.materials,
            "accessors": self.accessors,
            "bufferViews": self.buffer_views,
            "buffers": [{"uri": bin_path.name, "byteLength": len(self.bin)}],
            "samplers": self.samplers,
        }
        if self.images:
            doc["images"] = self.images
            doc["textures"] = self.textures
        path.write_text(json.dumps(doc), encoding="utf-8")


def _bsp_primitives(polys: List[BspPolygon], pkg: Package, resolver: MaterialResolver, writer: GltfWriter,
                    tex_dir: Path, tex_uri: Dict[str, Optional[str]], lighting: Optional[BspLighting] = None,
                    model_nodes=None, lm_uris: Optional[List[Optional[str]]] = None, lightmapped: Optional[list] = None):
    """Group polygons by (material, lightmap texture), triangulate as fans, compute UVs; TEXCOORD_1 = baked lightmap UV."""
    by_mat: Dict[Tuple[int, int], List[BspPolygon]] = defaultdict(list)
    for p in polys:
        if p.polyflags & PF_UNLIT:
            lm = -2        # fullbright surface: no lightmap, drawn at texture colour (plus the game's glow)
        else:
            lm = lighting.texture_for_node(p.node_index) if lighting is not None else -1
        by_mat[(p.material_ref, lm, bool(p.polyflags & PF_MIRRORED))].append(p)
    prims = []
    for (mref, lm, mirror), plist in by_mat.items():
        res = resolver.resolve(pkg, mref) if mref else None
        if res is not None and res.skip:
            continue
        key = res.key if res else "BSP.Default"
        uri = _ensure_texture(res, tex_dir, tex_uri)
        tw = th = 256.0
        if res is not None and res.texture is not None:
            tw, th = float(res.texture.width or 256), float(res.texture.height or 256)
        lm_uri = lm_uris[lm] if (lm_uris is not None and 0 <= lm < len(lm_uris)) else None
        mkey = f"{key}__lm{lm}" if lm_uri else (f"{key}__unlit" if lm == -2 else key)
        if mirror:
            mkey += "__mir"     # PF_Mirrored: a mirror surface in UE2 (MuseD, OffsD, ShipD, SteeD, TermD)
        mat = writer.material(mkey, res, uri, lightmap_uri=lm_uri)
        if mirror:
            writer.material_flags.setdefault(mkey, {"masked": False, "two_sided": False, "translucent": False})["mirror"] = True
        if lm == -2:
            writer.material_flags.setdefault(mkey, {"masked": bool(res.masked) if res else False,
                                                    "two_sided": bool(res.two_sided) if res else False,
                                                    "translucent": bool(res.translucent) if res else False})["unlit"] = True
        else:
            # Lightmapped surfaces too: their SpecularityMask limits the env-map energy / gloss, and a masked
            # SelfIllumination glows on top of the lightmap (MaskedSelfIllum in M_SCCT_Lightmapped).
            _self_illum_flags(writer, mkey, res, tex_dir, tex_uri)
        if lm_uri and lightmapped is not None and all(e["name"] != _safe(mkey) for e in lightmapped):
            lightmapped.append({"name": _safe(mkey), "base": key, "texture": uri, "lightmap": lm_uri,
                                "two_sided": bool(res.two_sided) if res else False,
                                "masked": bool(res.masked) if res else False, "translucent": bool(res.translucent) if res else False})
        pos_list, nrm_list, uv_list, uv2_list, idx_list = [], [], [], [], []
        base = 0
        for p in plist:
            n = len(p.vertices)
            rel = p.vertices - p.base
            u = rel @ p.texu / tw
            v = rel @ p.texv / th
            pos_list.append(p.vertices)
            nrm_list.append(np.repeat(p.normal[None, :], n, axis=0))
            uv_list.append(np.stack([u, v], axis=1))
            uv2 = None
            if lm_uri and lighting is not None and model_nodes is not None:
                sv = lighting.node_vertices(p.node_index, model_nodes[p.node_index].numverts)
                if sv is not None and len(sv) >= n:
                    # section vertices follow the node's pool order; extract_polygons may have dropped
                    # duplicate points, so match by position.
                    uv2 = np.zeros((n, 2), np.float32)
                    for k in range(n):
                        dist = np.abs(sv[:, :3] - p.vertices[k][None, :]).sum(axis=1)
                        j = int(np.argmin(dist))
                        uv2[k] = sv[j, 8:10]
            if uv2 is None:
                uv2 = np.zeros((n, 2), np.float32)
            uv2_list.append(uv2)
            for k in range(1, n - 1):
                idx_list.extend((base, base + k, base + k + 1))
            base += n
        if not pos_list:
            continue
        prims.append((np.concatenate(pos_list), np.concatenate(nrm_list),
                      _uv_transform(np.concatenate(uv_list).astype(np.float32), res),
                      np.array(idx_list, np.uint32), mat, np.concatenate(uv2_list).astype(np.float32) if lm_uri else None))
    return prims


def _rng(v, default):
    if isinstance(v, dict) and "Min" in v:
        return [float(v.get("Min", default)), float(v.get("Max", default))]
    if isinstance(v, (int, float)):
        return [float(v), float(v)]
    return [default, default]


def _rng3(v, default):
    if isinstance(v, dict) and "X" in v:
        return [_rng(v.get("X"), default), _rng(v.get("Y"), default), _rng(v.get("Z"), default)]
    return [[default, default]] * 3


_EMITTER_PNGS: Dict[str, bool] = {}   # PNG path -> written (emitter textures, decoded once per export)


def _export_emitters(pkg: Package, pset: PackageSet, tex_dir: Path, tex_uri: Dict[str, Optional[str]]) -> dict:
    """Placed particle emitters: {EmitterActorName: [sprite emitter params...]}. The Emitter actor's own
    properties do not parse on this build, so sub-objects are attributed to the nearest preceding Emitter
    export; the runtime places the effect at the live actor location."""
    from .properties import read_properties as _rp, skip_state_frame as _ssf
    out: Dict[str, list] = {}
    current = None
    for e in pkg.exports:
        if e.class_name == "Emitter" or e.class_name.endswith("Emitter") and e.class_name not in ("SpriteEmitter", "MeshEmitter", "BeamEmitter", "SparkEmitter"):
            current = e.name
            continue
        if e.class_name not in ("SpriteEmitter", "MeshEmitter") or current is None or e.size <= 0:
            continue
        try:
            r = pkg.export_reader(e)
            _ssf(r, e.flags)
            props = _rp(r)
        except Exception:
            props = {}
        entry = {
            "kind": e.class_name,
            "lifetime": _rng(props.get("LifetimeRange"), 2.0),
            "velocity": _rng3(props.get("StartVelocityRange"), 0.0),
            "size": _rng3(props.get("StartSizeRange"), 10.0),
            "acceleration": list(props.get("Acceleration")) if isinstance(props.get("Acceleration"), (tuple, list)) else [0.0, 0.0, 0.0],
            "spawn_extent": _rng3(props.get("StartLocationRange"), 0.0),
            "fade_out_start": float(props.get("FadeOutStartTime", 1.0) or 1.0),
            "size_scale": [(float(s.get("RelativeTime", 1.0)), float(s.get("RelativeSize", 1.0))) for s in props.get("SizeScale", []) if isinstance(s, dict)],
            "texture": None,
            # Disabled sub-emitters (TriggerDisabled event effects) draw nothing until the game enables them: AquaE's
            # fish tanks list a disabled extinguisher smoke first and their bubbles after it.
            "disabled": bool(props.get("Disabled", False)),
        }
        tref = props.get("Texture")
        if isinstance(tref, int) and tref != 0:
            try:
                owner, te = pset.resolve(pkg, tref)
                if te is not None and te.class_name == "Texture":
                    key = f"{owner.path.stem}.{te.name}"
                    fname = _safe(key) + ".png"
                    done = str(tex_dir / fname)
                    if done not in _EMITTER_PNGS:
                        # Decoded and written once per export: many emitters share a texture.
                        from .texture import read_texture
                        tx = read_texture(owner, te)
                        _EMITTER_PNGS[done] = tx is not None and tx.image is not None
                        if _EMITTER_PNGS[done]:
                            _write_png(tx.image, tex_dir / fname)
                    if _EMITTER_PNGS[done]:
                        entry["texture"] = f"textures/{fname}"
            except Exception:
                pass
        out.setdefault(current, []).append(entry)
    return out


def _read_color_stream(pkg: Package, ref: int) -> Optional[np.ndarray]:
    """StaticMeshInstance export: compact count + count x FColor (per-vertex baked lighting).

    UE2 serializes FColor as R,G,B,A (operator<<), unlike its B,G,R,A memory layout: reading it as BGRA turned
    Factory's warm light blue on 224 of 233 vertex-lit meshes."""
    if ref <= 0 or ref > len(pkg.exports):
        return None
    e = pkg.exports[ref - 1]
    if e.class_name != "StaticMeshInstance" or e.size <= 0:
        return None
    try:
        r = pkg.export_reader(e)
        from .properties import read_properties as _rp, skip_state_frame as _ssf
        _ssf(r, e.flags)
        _rp(r)
        n = r.compact()
        if n <= 0 or n * 4 > r.remaining():
            return None
        return np.frombuffer(r.raw(n * 4), dtype=np.uint8).reshape(n, 4).copy()
    except Exception:
        return None


def _write_png(image, out: Path) -> None:
    """Write a PNG only when its bytes changed: a decoder fix must reach the export (an existence check kept stale
    images), while unchanged files keep their timestamps."""
    import io
    buf = io.BytesIO()
    image.save(buf, format="PNG")
    data = buf.getvalue()
    if out.exists() and out.stat().st_size == len(data) and out.read_bytes() == data:
        return
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(data)


def _texture_png(key: str, texture, tex_dir: Path, tex_uri: Dict[str, Optional[str]]) -> Optional[str]:
    """PNG for a texture (or a material resolved to one) named after `key`; returns its glTF-relative URI."""
    if texture is None or texture.image is None:
        return None
    if key in tex_uri:
        return tex_uri[key]
    fname = _safe(key) + ".png"
    _write_png(texture.image, tex_dir / fname)
    uri = f"textures/{fname}"
    tex_uri[key] = uri
    return uri


def _flipbook_png(key: str, flipbook: dict, tex_dir: Path, tex_uri: Dict[str, Optional[str]]) -> Optional[dict]:
    """Atlas of an animated texture's frames (row-major, cols x rows cells of the frame size) as <key>__flip.png;
    returns {"texture", "cols", "rows", "frames", "fps"}."""
    from PIL import Image
    frames = [f.image for f in flipbook.get("frames") or [] if f is not None and f.image is not None]
    if len(frames) < 2:
        return None
    n = len(frames)
    cols = int(math.ceil(math.sqrt(n)))
    rows = int(math.ceil(n / cols))
    w, h = frames[0].size
    fkey = key + "__flip"
    if fkey not in tex_uri:
        atlas = Image.new("RGBA", (cols * w, rows * h))
        for i, im in enumerate(frames):
            atlas.paste(im.convert("RGBA"), ((i % cols) * w, (i // cols) * h))
        fname = _safe(fkey) + ".png"
        _write_png(atlas, tex_dir / fname)
        tex_uri[fkey] = f"textures/{fname}"
    return {"texture": tex_uri[fkey], "cols": cols, "rows": rows, "frames": n, "fps": float(flipbook.get("fps") or 30.0)}


def _with_opacity(diffuse, opacity):
    """Diffuse RGB with the Shader.Opacity texture as alpha (its alpha channel when it varies, else its luminance),
    resampled to the diffuse size."""
    from PIL import Image
    base = diffuse.convert("RGBA")
    op = opacity.convert("RGBA").resize(base.size, Image.BILINEAR)
    a = op.getchannel("A")
    lo, hi = a.getextrema()
    alpha = a if hi - lo > 8 else op.convert("L")
    base.putalpha(alpha)
    return base


def _combined_image(res: ResolvedMaterial):
    """UE2 Combiner baked to one RGBA PIL image at Material1's size."""
    c = res.combine
    if not c or res.texture is None or res.texture.image is None:
        return None
    a = np.asarray(res.texture.image, dtype=np.float32) / 255.0
    h, w = a.shape[:2]

    def fit(tex):
        if tex is None or tex.image is None:
            return None
        im = np.asarray(tex.image)
        if im.shape[:2] != (h, w):
            from PIL import Image
            im = np.asarray(Image.fromarray(im).resize((w, h), Image.BILINEAR))
        return im.astype(np.float32) / 255.0
    b = fit(c.get("tex2"))
    m = fit(c.get("mask"))
    if b is None:
        b = a
    if m is None:
        m = a
    ma = m[..., 3:4] if m.shape[-1] == 4 else np.ones_like(a[..., :1])
    if c.get("invert_mask"):
        ma = 1.0 - ma
    op = c.get("op", 0)
    rgb1, rgb2 = a[..., :3], b[..., :3]
    if op == 1:
        rgb = rgb2
    elif op == 2:
        rgb = rgb1 * rgb2
    elif op == 3:
        rgb = rgb1 + rgb2
    elif op == 4:
        rgb = rgb1 - rgb2
    elif op == 5:
        rgb = rgb1 * ma + rgb2 * (1.0 - ma)
    elif op == 6:
        rgb = rgb1 + rgb2 * ma
    elif op == 7:
        rgb = m[..., :3]
    else:
        rgb = rgb1
    rgb = rgb * float(c.get("mod", 1))
    a1 = a[..., 3:4] if a.shape[-1] == 4 else np.ones_like(rgb[..., :1])
    a2 = b[..., 3:4] if b.shape[-1] == 4 else np.ones_like(rgb[..., :1])
    aop = c.get("alpha_op", 0)
    alpha = {0: ma, 1: a1 * a2, 2: a1 + a2, 3: a1, 4: a2}.get(aop, ma)
    out = np.concatenate([np.clip(rgb, 0, 1), np.clip(alpha, 0, 1)], axis=-1)
    from PIL import Image
    return Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), "RGBA")


def _combine_flags(key: str, res: ResolvedMaterial, tex_dir: Optional[Path], tex_uri) -> Optional[dict]:
    """A Combiner whose inputs carry their own texture modifiers (AquaE's sea: two TexPanners at different rates): the
    inputs as separate PNGs with their UV animation, for the importer's Combine switch. "texture" stays the t=0 bake
    (fallback). Schema: {"op", "alpha_op", "mod", "invert_mask", "tex1", "tex2", "mask" (absent: Material1),
    "anim1", "anim2", "animMask" (uv_anim dicts or null), "matrix1", "matrix2", "matrixMask" (static 2x3 or null)}. The
    importer (lighting-balance d8de23b, parents v16) reads op/alpha_op/mod/tex1/tex2/mask/anim1/anim2/animMask/invert_mask."""
    c = getattr(res, "combine", None)
    if not c or tex_dir is None or tex_uri is None or res.texture is None:
        return None
    anims = c.get("anims") or {}
    if not any(a for a, _ in anims.values()):
        return None
    out = {"op": c.get("op", 0), "alpha_op": c.get("alpha_op", 0), "mod": c.get("mod", 1),
           "invert_mask": bool(c.get("invert_mask"))}
    for slot, tex in (("1", res.texture), ("2", c.get("tex2")), ("mask", c.get("mask"))):
        name = "tex1" if slot == "1" else ("tex2" if slot == "2" else "mask")
        uri = _texture_png(f"{key}__c{slot}", tex, tex_dir, tex_uri) if tex is not None else None
        if uri is None and slot != "mask":
            return None
        if uri is not None:
            out[name] = uri
        anim, matrix = anims.get(slot, (None, None))
        suffix = slot if slot != "mask" else "Mask"
        out[f"anim{suffix}"] = anim
        out[f"matrix{suffix}"] = list(matrix) if matrix is not None else None
    return out


def _ensure_texture(res: Optional[ResolvedMaterial], tex_dir: Path, tex_uri: Dict[str, Optional[str]]) -> Optional[str]:
    if res is None:
        return None
    if getattr(res, "combine", None):
        key = f"{res.key}__comb"
        if key not in tex_uri:
            img = _combined_image(res)
            if img is not None:
                fname = _safe(key) + ".png"
                _write_png(img, tex_dir / fname)
                tex_uri[key] = f"textures/{fname}"
            else:
                tex_uri[key] = None
        if tex_uri[key]:
            return tex_uri[key]
    if getattr(res, "opacity", None) is not None and res.texture is not None and res.texture.image is not None             and res.opacity.image is not None:
        key = f"{res.key}__op"
        if key not in tex_uri:
            fname = _safe(key) + ".png"
            _write_png(_with_opacity(res.texture.image, res.opacity.image), tex_dir / fname)
            tex_uri[key] = f"textures/{fname}"
        return tex_uri[key]
    return _texture_png(res.key, res.texture, tex_dir, tex_uri)


def _self_illum_flags(writer: "GltfWriter", key: str, res: Optional[ResolvedMaterial], tex_dir: Path,
                      tex_uri: Dict[str, Optional[str]]) -> None:
    """Shader.SelfIllumination with a SelfIlluminationMask: the importer builds a masked-emissive instance
    (M_SCCT_SelfIllum: emissive = SelfIllumination.rgb x Mask.a) instead of drawing the whole surface fullbright."""
    if res is not None and getattr(res, "spec_mask", None) is not None and key in writer.material_flags:
        # Env-mapped shader's SpecularityMask (gun metal): the importer limits the gloss to its alpha.
        uri = _texture_png(res.spec_mask_key, res.spec_mask, tex_dir, tex_uri)
        if uri:
            writer.material_flags[key]["spec_mask"] = uri
    if res is None or res.self_illum is None or res.self_illum_mask is None or key not in writer.material_flags:
        return
    si = _texture_png(res.self_illum_key, res.self_illum, tex_dir, tex_uri)
    mask = _texture_png(res.self_illum_mask_key, res.self_illum_mask, tex_dir, tex_uri)
    if si and mask:
        writer.material_flags[key].update({"self_illum": si, "self_illum_mask": mask})
        if getattr(res, "self_illum_anim", False) and res.uv_anim:
            writer.material_flags[key]["self_illum_anim"] = True


def _uv_transform(uv: Optional[np.ndarray], res: Optional[ResolvedMaterial]) -> Optional[np.ndarray]:
    """Bake a static TexScaler/TexRotator matrix into a primitive's UVs (u' = a*u + c*v + e, v' = b*u + d*v + f)."""
    if uv is None or res is None or res.uv_matrix is None:
        return uv
    a, b, c, d, e, f = res.uv_matrix
    out = np.empty_like(uv, dtype=np.float32)
    out[:, 0] = a * uv[:, 0] + c * uv[:, 1] + e
    out[:, 1] = b * uv[:, 0] + d * uv[:, 1] + f
    return out


def _section_material(sm: StaticMesh, si: int, owner: Package, resolver: MaterialResolver,
                      skins: Optional[List[int]] = None, skin_pkg: Optional[Package] = None) -> Optional[ResolvedMaterial]:
    """Material of mesh section si: the actor's Skins[si] when set (UE2 Actor.GetSkin), else the mesh's own."""
    if skins and skin_pkg is not None and si < len(skins) and skins[si]:
        return resolver.resolve(skin_pkg, skins[si])
    sec = sm.sections[si]
    return resolver.resolve(owner, sec.material_ref) if sec.material_ref else None


def _static_mesh_prims(sm: StaticMesh, owner: Package, resolver: MaterialResolver, writer: GltfWriter,
                       tex_dir: Path, tex_uri: Dict[str, Optional[str]], skins: Optional[List[int]] = None,
                       skin_pkg: Optional[Package] = None, unlit: bool = False):
    """One primitive per mesh section. `skins`: per-section object refs in `skin_pkg` (the map) replacing the
    section materials, as the game does for Actor.Skins. `unlit`: Actor.bUnlit, every section fullbright
    (material key <key>__unlit, like BSP PF_Unlit surfaces)."""
    prims = []
    uv = sm.uvs[0] if sm.uvs else None
    for si, sec in enumerate(sm.sections):
        if sec.num_faces <= 0:
            continue
        idx = sm.indices[sec.first_index:sec.first_index + sec.num_faces * 3]
        if len(idx) < 3:
            continue
        res = _section_material(sm, si, owner, resolver, skins, skin_pkg)
        if res is not None and res.skip:
            continue
        key = res.key if res else f"{owner.path.stem}.{sm.name}.default{si}"
        uri = _ensure_texture(res, tex_dir, tex_uri)
        if unlit:
            key = f"{key}__unlit"
            mat = writer.material(key, res, uri)
            writer.material_flags.setdefault(key, {"masked": bool(res.masked) if res else False,
                                                   "two_sided": bool(res.two_sided) if res else False,
                                                   "translucent": bool(res.translucent) if res else False})["unlit"] = True
        else:
            mat = writer.material(key, res, uri)
            _self_illum_flags(writer, key, res, tex_dir, tex_uri)
        prims.append((sm.positions, sm.normals, _uv_transform(uv, res), idx.astype(np.uint32), mat))
    return prims


def _patch_prims(patch: dict, res, writer: GltfWriter, tex_dir: Path, tex_uri: Dict[str, Optional[str]], unlit: bool):
    """Flat nb_u x nb_v grid in the actor's local XZ plane (U along X, V down Z), centred, both faces; UVs span
    scale_u x scale_v texture repeats."""
    nu, nv = max(1, int(patch["nb_u"])), max(1, int(patch["nb_v"]))
    w, h = nu * float(patch["u_length"]), nv * float(patch["v_length"])
    iu, iv = np.meshgrid(np.arange(nu + 1), np.arange(nv + 1), indexing="xy")
    fu, fv = (iu / nu).ravel(), (iv / nv).ravel()
    pos = np.stack([(fu - 0.5) * w, np.zeros_like(fu), (0.5 - fv) * h], axis=1).astype(np.float32)
    uv = np.stack([fu * float(patch.get("scale_u", 1.0) or 1.0), fv * float(patch.get("scale_v", 1.0) or 1.0)], axis=1).astype(np.float32)
    quads = []
    for j in range(nv):
        for i in range(nu):
            a, b = j * (nu + 1) + i, j * (nu + 1) + i + 1
            c, d = a + nu + 1, b + nu + 1
            quads += [a, c, b, b, c, d]
    front = np.array(quads, np.uint32)
    back = front.reshape(-1, 3)[:, ::-1].ravel()
    n = len(pos)
    positions = np.concatenate([pos, pos])
    normals = np.concatenate([np.tile([0.0, -1.0, 0.0], (n, 1)), np.tile([0.0, 1.0, 0.0], (n, 1))]).astype(np.float32)
    uvs = np.concatenate([uv, uv])
    idx = np.concatenate([front, back + n]).astype(np.uint32)
    key = res.key + ("__unlit" if unlit else "")
    uri = _ensure_texture(res, tex_dir, tex_uri)
    mat = writer.material(key, res, uri)
    if unlit:
        writer.material_flags.setdefault(key, {"masked": bool(res.masked), "two_sided": bool(res.two_sided),
                                               "translucent": bool(res.translucent)})["unlit"] = True
    return [(positions, normals, _uv_transform(uvs, res), idx, mat)]


def _skin_signature(sm: StaticMesh, owner: Package, resolver: MaterialResolver, skins: List[int], skin_pkg: Package) -> str:
    """Stable id of the effective section materials when Skins change any of them ('' when they change nothing)."""
    changed = False
    keys = []
    for si in range(len(sm.sections)):
        base = _section_material(sm, si, owner, resolver)
        eff = _section_material(sm, si, owner, resolver, skins, skin_pkg)
        bk, ek = (base.key if base else ""), (eff.key if eff else "")
        changed = changed or bk != ek
        keys.append(ek)
    return ",".join(keys) if changed else ""


def export_package(usx_path: Path, pset: PackageSet, out_root: Path, extra_materials: Optional[List[str]] = None) -> dict:
    """Export every StaticMesh of one .usx package as library nodes (for class-default meshes such as
    SGameplayObjects: medkits, cameras, laser emitters, objectives). Output: out/_shared/<Pkg>/<Pkg>.gltf.
    `extra_materials`: object paths (Package[.Group].Name) of materials no mesh section uses but actors put in their
    Skins at run time (camera and sensor LEDs); each becomes a glTF material like a section's, and stats["skins"]
    maps the path to its material name."""
    pkg = Package.load(usx_path)
    name = usx_path.stem
    out_dir = out_root / name
    out_dir.mkdir(parents=True, exist_ok=True)
    tex_dir = out_dir / "textures"
    tex_uri: Dict[str, Optional[str]] = {}
    resolver = MaterialResolver(pset)
    writer = GltfWriter()
    stats = {"package": name, "meshes": 0, "failed": [], "skins": {}}
    for path in extra_materials or []:
        pkg_name, _, rest = path.partition(".")
        owner = pset.get(pkg_name)
        e = owner.find_export(rest.split(".")[-1]) if owner is not None and rest else None
        if e is None:
            stats["failed"].append(f"{path}: not found")
            continue
        try:
            res = resolver.resolve(owner, e.index + 1)
            if res is None or res.skip:
                continue
            writer.material(res.key, res, _ensure_texture(res, tex_dir, tex_uri))
            _self_illum_flags(writer, res.key, res, tex_dir, tex_uri)
            stats["skins"][path] = res.key
        except Exception as ex:
            stats["failed"].append(f"{path}: {ex}")
    for e in pkg.exports_of_class("StaticMesh"):
        key = f"{name}.{e.name}"
        try:
            sm = read_static_mesh(pkg, e)
            prims = _static_mesh_prims(sm, pkg, resolver, writer, tex_dir, tex_uri)
            if prims:
                mi = writer.mesh(_safe(key), prims)
                writer.node(_safe(key), mi, extras={"scct_kind": "library"})
                stats["meshes"] += 1
        except Exception as ex:
            stats["failed"].append(f"{key}: {ex}")
    gltf_path = out_dir / f"{name}.gltf"
    writer.write(gltf_path)
    # material_flags let import_shared.py give these runtime meshes (no baked lighting) the same unlit /
    # self-illuminated / masked instances as map meshes: the first-aid cabinet's red cross, terminal screens.
    (out_dir / "scene.json").write_text(json.dumps({"package": name, "gltf": gltf_path.name,
                                                    "additive_materials": _additive_list(writer), "modulate_materials": _modulate_list(writer),
                                                    "material_flags": {_safe(k): v for k, v in writer.material_flags.items()},
                                                    "lightmapped_materials": [], "vertex_lit_meshes": [],
                                                    "stats": {"meshes": stats["meshes"]}, "failed": stats["failed"][:100]}, indent=1), encoding="utf-8")
    return stats


def _modulate_list(writer: GltfWriter) -> list:
    out = []
    for key, idx in writer.material_index.items():
        m = writer.materials[idx]
        if m.get("extras", {}).get("scct_blend") == "modulate":
            tex = None
            if "emissiveTexture" in m:
                tex = writer.images[writer.textures[m["emissiveTexture"]["index"]]["source"]]["uri"]
            entry = {"key": key, "name": _safe(key), "texture": tex, "mode": m["extras"].get("scct_modulate", "2x")}
            if writer.material_flags.get(key, {}).get("uv_anim"):
                entry["uv_anim"] = writer.material_flags[key]["uv_anim"]
            out.append(entry)
    return out


def _additive_list(writer: GltfWriter) -> list:
    additive = []
    for key, idx in writer.material_index.items():
        m = writer.materials[idx]
        if m.get("extras", {}).get("scct_blend") == "additive":
            tex = None
            if "emissiveTexture" in m:
                tex = writer.images[writer.textures[m["emissiveTexture"]["index"]]["source"]]["uri"]
            entry = {"key": key, "name": _safe(key), "texture": tex}
            if "scct_add_gain" in m["extras"]:
                entry["gain"] = m["extras"]["scct_add_gain"]
            if "scct_flipbook" in m["extras"]:
                entry["flipbook"] = m["extras"]["scct_flipbook"]
            if writer.material_flags.get(key, {}).get("uv_anim"):
                entry["uv_anim"] = writer.material_flags[key]["uv_anim"]
            additive.append(entry)
    return additive


def export_textures(names: List[str], pset: PackageSet, out_dir: Path) -> List[str]:
    """Export textures or materials by "Package.Name" (a material is resolved to its diffuse texture) as
    <out_dir>/<Package>_<Name>.png, e.g. the goggle-beam skins the game only references through runtime Skins[0]."""
    resolver = MaterialResolver(pset)
    written = []
    for full in names:
        pkg_name, _, obj = full.partition(".")
        pkg = pset.get(pkg_name)
        e = pkg.find_export(obj.split(".")[-1]) if pkg is not None else None
        if e is None:
            continue
        res = resolver.resolve(pkg, e.index + 1)
        if res.texture is not None and res.texture.image is not None:
            out = out_dir / (_safe(f"{pkg_name}.{e.name}") + ".png")
            _write_png(res.texture.image, out)
            written.append(str(out))
    return written


def _rot_axes(rot) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
    """UE2 GetAxes / FRotationMatrix rows for a (Pitch, Yaw, Roll) rotator in 65536 units."""
    p, y, r = (float(v) * np.pi / 32768.0 for v in rot)
    sp, cp, sy, cy, sr, cr = np.sin(p), np.cos(p), np.sin(y), np.cos(y), np.sin(r), np.cos(r)
    x_axis = np.array([cp * cy, cp * sy, sp])
    y_axis = np.array([sr * sp * cy - cr * sy, sr * sp * sy + cr * cy, -sr * cp])
    z_axis = np.array([-(cr * sp * cy + sr * sy), cy * sr - cr * sp * sy, cr * cp])
    return x_axis, y_axis, z_axis


def _clip_poly(rows: np.ndarray, col: int, limit: float, keep_above: bool) -> np.ndarray:
    """Sutherland-Hodgman against one axis-aligned plane in the projector's (u, v, depth) columns."""
    if len(rows) == 0:
        return rows
    out = []
    n = len(rows)
    for i in range(n):
        a, b = rows[i], rows[(i + 1) % n]
        da = (a[col] - limit) if keep_above else (limit - a[col])
        db = (b[col] - limit) if keep_above else (limit - b[col])
        if da >= 0:
            out.append(a)
        if (da >= 0) != (db >= 0):
            t = da / (da - db)
            out.append(a + (b - a) * t)
    return np.array(out) if out else np.zeros((0, rows.shape[1]))


PROJECTOR_SURFACE_OFFSET = 0.3   # uu the decal floats above the wall (UE5 depth precision handles this)


def _projector_prims(rec: ActorRecord, polys: List[BspPolygon], pkg: Package, resolver: MaterialResolver,
                     writer: GltfWriter, tex_dir: Path, tex_uri: Dict[str, Optional[str]]):
    """UE2 orthographic texture projector (FOV 0) clipped onto the BSP it covers, as a decal mesh.

    Mapping verified against the live Projector.Matrix / FrustumVertices (MissE Projector325):
    u = dot(P-L, Y) / (USize * DrawScale * DrawScale3D.Y) + 0.5, v = dot(P-L, Z) / (VSize * DrawScale *
    DrawScale3D.Z) + 0.5, depth = dot(P-L, X) in [0, MaxTraceDistance], with X/Y/Z the rotator axes."""
    x = rec.extra
    if x.get("fov", 0) != 0:
        return _perspective_projector_prims(rec, polys, pkg, resolver, writer, tex_dir, tex_uri)
    if not x.get("project_bsp", True) or x.get("fb_blend") == 1:
        return None, None       # ortho modulate projectors are not reproduced
    res = resolver.resolve(pkg, x["proj_texture_ref"])
    if res.skip or res.texture is None:
        return None, None
    tw = float(res.texture.props.get("USize") or res.texture.width or 256)
    th = float(res.texture.props.get("VSize") or res.texture.height or 256)
    loc = np.array(rec.location, np.float64)
    ax, ay, az = _rot_axes(rec.rotation)
    su, sv = tw * rec.scale[1], th * rec.scale[2]       # rec.scale = DrawScale * DrawScale3D (signed)
    depth = float(x.get("max_trace", 1000.0))
    pos_list, nrm_list, uv_list, idx_list = [], [], [], []
    base = 0
    for p in polys:
        facing = float(np.dot(p.normal, ax))
        if facing > -0.01 and not x.get("backfaces"):
            continue    # faces away from the projector (UE2 bProjectOnBackfaces / bProjectOnParallelBSP off)
        d = p.vertices.astype(np.float64) - loc
        rows = np.column_stack([d @ ay / su + 0.5, d @ az / sv + 0.5, d @ ax, p.vertices.astype(np.float64)])
        if (rows[:, 0].max() < 0 or rows[:, 0].min() > 1 or rows[:, 1].max() < 0 or rows[:, 1].min() > 1
                or rows[:, 2].max() < 0 or rows[:, 2].min() > depth):
            continue
        for col, lim, above in ((0, 0.0, True), (0, 1.0, False), (1, 0.0, True), (1, 1.0, False), (2, 0.0, True), (2, depth, False)):
            rows = _clip_poly(rows, col, lim, above)
            if len(rows) < 3:
                break
        if len(rows) < 3:
            continue
        n = len(rows)
        pos_list.append(rows[:, 3:6] + p.normal[None, :].astype(np.float64) * PROJECTOR_SURFACE_OFFSET)
        nrm_list.append(np.repeat(p.normal[None, :], n, axis=0))
        uv_list.append(rows[:, 0:2])
        for k in range(1, n - 1):
            idx_list.extend((base, base + k, base + k + 1))
        base += n
    if not pos_list:
        return None, None
    fb = int(x.get("fb_blend", 2))
    key = f"PROJ.{res.key}" + ("__add" if fb == 3 else "")
    uri = _ensure_texture(res, tex_dir, tex_uri)
    # PB_AlphaBlend (2) and PB_None (0, overwrite: an alpha-less texture then covers its whole rectangle) draw the
    # texture over the wall with its alpha, unlit; PB_Add (3) goes through the additive path.
    from dataclasses import replace
    # Never flagged additive: the importer's generic M_Add_ swap (gain 0.6) would take the slot before the projector
    # instance (M_SCCT_ProjectorAdd for PB_Add) is applied.
    pres = replace(res, key=key, translucent=fb != 3, additive=False, masked=False, two_sided=False, unlit=False,
                   uv_matrix=None, self_illum=None, self_illum_mask=None)
    mat = writer.material(key, pres, uri)
    writer.material_flags[key].update({"projector": True, "blend": fb})
    prim = (np.concatenate(pos_list).astype(np.float32), np.concatenate(nrm_list).astype(np.float32),
            np.concatenate(uv_list).astype(np.float32), np.array(idx_list, np.uint32), mat)
    return [prim], key


def _perspective_projector_prims(rec: ActorRecord, polys: List[BspPolygon], pkg: Package, resolver: MaterialResolver,
                                 writer: GltfWriter, tex_dir: Path, tex_uri: Dict[str, Optional[str]]):
    """UE2 perspective projector (FOV > 0; the Club maps' pool caustics, PB_Modulate) as a decal mesh over the BSP inside
    its frustum. The UVs are computed per pixel by M_SCCT_ProjectorPersp from the projector's origin and axes (material
    flags "persp"), so large polygons are not warped; the mesh only has to cover the lit area.
    Frustum: depth = dot(P-L, X) in (0, MaxTraceDistance], |dot(P-L, Y)| <= t * depth, |dot(P-L, Z)| <= t * a * depth with
    t = tan(FOV / 2) and a = VSize / USize."""
    x = rec.extra
    if not x.get("project_bsp", True):
        return None, None
    res = resolver.resolve(pkg, x["proj_texture_ref"])
    if res.skip or res.texture is None:
        return None, None
    tw = float(res.texture.props.get("USize") or res.texture.width or 256)
    th = float(res.texture.props.get("VSize") or res.texture.height or 256)
    loc = np.array(rec.location, np.float64)
    ax, ay, az = _rot_axes(rec.rotation)
    t = float(np.tan(np.radians(min(170.0, float(x.get("fov", 60.0))) * 0.5)))
    a = th / tw
    depth = float(x.get("max_trace", 1000.0))
    pos_list, nrm_list, uv_list, idx_list = [], [], [], []
    base = 0
    for p in polys:
        if float(np.dot(p.normal, ax)) > -0.01 and not x.get("backfaces"):
            continue
        d = p.vertices.astype(np.float64) - loc
        dx, dy, dz = d @ ax, d @ ay, d @ az
        rows = np.column_stack([t * dx + dy, t * dx - dy, t * a * dx + dz, t * a * dx - dz, dx, depth - dx,
                                p.vertices.astype(np.float64)])
        for col in range(6):
            rows = _clip_poly(rows, col, 0.0, True)
            if len(rows) < 3:
                break
        if len(rows) < 3:
            continue
        n = len(rows)
        v = rows[:, 6:9]
        dd = v - loc
        ddx = np.maximum(dd @ ax, 1e-3)
        uv = np.column_stack([0.5 + (dd @ ay) / (2 * t * ddx), 0.5 + (dd @ az) / (2 * t * a * ddx)])
        pos_list.append(v + p.normal[None, :].astype(np.float64) * PROJECTOR_SURFACE_OFFSET)
        nrm_list.append(np.repeat(p.normal[None, :], n, axis=0))
        uv_list.append(uv)
        for k in range(1, n - 1):
            idx_list.extend((base, base + k, base + k + 1))
        base += n
    if not pos_list:
        return None, None
    fb = int(x.get("fb_blend", 1))
    key = f"PROJ.{res.key}__persp_{rec.name}"
    uri = _ensure_texture(res, tex_dir, tex_uri)
    from dataclasses import replace
    pres = replace(res, key=key, translucent=fb not in (1, 3), additive=False, modulate="", masked=False, two_sided=False,
                   unlit=False, uv_matrix=None, self_illum=None, self_illum_mask=None)
    mat = writer.material(key, pres, uri)
    writer.material_flags[key].update({"projector": True, "blend": fb, "persp": {
        "origin": [float(c) for c in loc], "x": [float(c) for c in ax], "y": [float(c) for c in ay],
        "z": [float(c) for c in az], "tan": t, "aspect": a}})
    prim = (np.concatenate(pos_list).astype(np.float32), np.concatenate(nrm_list).astype(np.float32),
            np.concatenate(uv_list).astype(np.float32), np.array(idx_list, np.uint32), mat)
    return [prim], key


def export_map(map_path: Path, pset: PackageSet, out_root: Path, include_bsp: bool = True,
               max_meshes: Optional[int] = None, extra_mesh_refs: Optional[List[int]] = None) -> dict:
    """extra_mesh_refs: static mesh references (relative to the map package) that no actor places but the game can
    swap to (SGamePlayObject TriggeredMesh, DamagedMeshes[].StaticMesh); each becomes a library node too."""
    pkg = Package.load(map_path)
    map_name = map_path.stem
    out_dir = out_root / map_name
    out_dir.mkdir(parents=True, exist_ok=True)
    tex_dir = out_dir / "textures"
    tex_uri: Dict[str, Optional[str]] = {}
    resolver = MaterialResolver(pset)
    writer = GltfWriter()
    writer.tex_dir, writer.tex_uri = tex_dir, tex_uri   # flipbook atlases (animated additive textures)
    stats = {"map": map_name, "bsp_polys": 0, "static_meshes": 0, "actors": 0, "missing_meshes": []}
    bsp_actor = None

    lightmapped: list = []
    vertex_lit: list = []
    authored_vc: list = []   # vertex-colour variants from StaticMesh.UseVertexColor (authored colours, not baked light)
    emitters = _export_emitters(pkg, pset, tex_dir, tex_uri)
    records = extract_level(pkg, pset)
    # UE2 sky zone: the zone holding the SkyZoneInfo is rendered behind everything from a camera at the SkyZoneInfo's
    # location with the player's rotation (seen through fake-backdrop surfaces). Its BSP and actors are exported
    # separately so the client can draw them as a camera-following backdrop.
    sky_rec = next((r for r in records if r.cls == "SkyZoneInfo"), None)
    sky_zone: Optional[int] = None
    sky_actor = None
    model = None
    world_polys: List[BspPolygon] = []   # projector receivers
    # world BSP
    if include_bsp:
        models = [e for e in pkg.exports_of_class("Model") if e.size > 0]
        if models:
            world = max(models, key=lambda e: e.size)
            model = read_model(pkg, world)
            polys = extract_polygons(model)
            sky_polys: List[BspPolygon] = []
            if sky_rec is not None:
                sky_zone = point_zone(model, sky_rec.location)
                # The sky zone's own walls are fake-backdrop surfaces (SteeE's star box, SQU13_txt.Tsky06): drawn by
                # the sky view, while the fake-backdrop windows of the other zones stay out.
                sky_polys = [p for p in extract_polygons(model, include_fake_backdrop=True)
                             if model.nodes[p.node_index].izone[1] == sky_zone]
                if sky_polys and len(sky_polys) < len(polys):
                    polys = [p for p in polys if model.nodes[p.node_index].izone[1] != sky_zone]
                else:
                    sky_polys = []
                stats["sky_zone"] = sky_zone
                stats["sky_polys"] = len(sky_polys)
            stats["bsp_polys"] = len(polys)
            world_polys = polys
            lighting = None
            lm_uris: List[Optional[str]] = []
            try:
                lighting = read_bsp_lighting(pkg, world)
                tex_dir.mkdir(parents=True, exist_ok=True)
                for i, lt in enumerate(lighting.textures):
                    fname = f"{_safe(map_name)}_lightmap{i}.png"
                    lightmap_image(lt).save(tex_dir / fname)
                    lm_uris.append(f"textures/{fname}")
                stats["lightmaps"] = len(lm_uris)
            except Exception as ex:
                stats["lightmap_error"] = str(ex)
            prims = _bsp_primitives(polys, pkg, resolver, writer, tex_dir, tex_uri, lighting, model.nodes, lm_uris, lightmapped)
            if prims:
                mi = writer.mesh(f"BSP_{map_name}", prims)
                writer.node(f"BSP_{map_name}", mi, extras={"scct_kind": "bsp"})
                bsp_actor = {"name": f"BSP_{map_name}", "class": "Model", "kind": "bsp", "location": [0.0, 0.0, 0.0],
                             "rotation": [0, 0, 0], "scale": [1.0, 1.0, 1.0], "tag": "", "hidden": False,
                             "mesh": f"BSP_{map_name}", "mesh_available": True}
            if sky_polys:
                sprims = _bsp_primitives(sky_polys, pkg, resolver, writer, tex_dir, tex_uri, lighting, model.nodes, lm_uris, lightmapped)
                if sprims:
                    smi = writer.mesh(f"BSP_{map_name}_Sky", sprims)
                    writer.node(f"BSP_{map_name}_Sky", smi, extras={"scct_kind": "bsp"})
                    sky_actor = {"name": f"BSP_{map_name}_Sky", "class": "Model", "kind": "bsp", "location": [0.0, 0.0, 0.0],
                                 "rotation": [0, 0, 0], "scale": [1.0, 1.0, 1.0], "tag": "", "hidden": False,
                                 "mesh": f"BSP_{map_name}_Sky", "mesh_available": True, "sky_zone": True}

    # actors
    stats["actors"] = len(records)
    mesh_cache: Dict[str, Optional[int]] = {}
    sm_cache: Dict[str, Optional[Tuple[Package, StaticMesh]]] = {}
    scene_actors = [a for a in (bsp_actor, sky_actor) if a]
    color_variants: Dict[str, Optional[str]] = {}   # "<key>|<hash>|<skins>" -> node name
    skin_variants: Dict[str, Optional[str]] = {}    # "<key>|<skins>" -> node name

    def load_mesh(rec: ActorRecord) -> Optional[Tuple[Package, StaticMesh]]:
        key = rec.mesh_key
        if key not in sm_cache:
            sm_cache[key] = None
            owner, e = pset.resolve(pkg, rec.mesh_ref)
            if owner is not None and e is not None and e.class_name == "StaticMesh":
                try:
                    sm_cache[key] = (owner, read_static_mesh(owner, e))
                except Exception as ex:
                    stats["missing_meshes"].append(f"{key}: {ex}")
            else:
                stats["missing_meshes"].append(key)
        return sm_cache[key]

    for rec in records:
        entry = {"name": rec.name, "class": rec.cls, "kind": rec.kind, "location": list(rec.location),
                 "rotation": list(rec.rotation), "scale": list(rec.scale), "tag": rec.tag, "hidden": rec.hidden}
        if sky_zone is not None and model is not None and rec.cls != "SkyZoneInfo" and point_zone(model, rec.location) == sky_zone:
            entry["sky_zone"] = True
        if rec.kind in ("staticmesh", "mover") and rec.mesh_key:
            key = rec.mesh_key
            if key not in mesh_cache:
                mi = None
                if max_meshes is None or len(mesh_cache) < max_meshes:
                    loaded = load_mesh(rec)
                    if loaded is not None:
                        owner, sm = loaded
                        try:
                            prims = _static_mesh_prims(sm, owner, resolver, writer, tex_dir, tex_uri)
                            if prims:
                                mi = writer.mesh(_safe(key), prims)
                                # library node at identity: Interchange bakes node transforms into the mesh asset
                                writer.node(_safe(key), mi, extras={"scct_kind": "library"})
                                stats["static_meshes"] += 1
                        except Exception as ex:
                            stats["missing_meshes"].append(f"{key}: {ex}")
                mesh_cache[key] = mi
            entry["mesh"] = _safe(key)
            entry["mesh_available"] = mesh_cache.get(key) is not None
            loaded = sm_cache.get(key) if mesh_cache.get(key) is not None else None
            # Actor.Skins: a mesh variant whose sections use the skin materials (FactD WORD_ARROW sign planes show
            # CORRIDORS / MAINHALL / ROOM ..., MissE exit lights their colour).
            skin_refs = rec.extra.get("skin_refs") or []
            skin_sig, skin_id = "", ""
            if loaded is not None and any(skin_refs):
                owner, sm = loaded
                skin_sig = _skin_signature(sm, owner, resolver, skin_refs, pkg)
                if skin_sig:
                    skin_id = hashlib.md5(skin_sig.encode("utf-8")).hexdigest()[:6]
                    skey = f"{key}|{skin_sig}"
                    if skey not in skin_variants:
                        sname = _safe(key) + f"__sk{skin_id}"
                        prims = _static_mesh_prims(sm, owner, resolver, writer, tex_dir, tex_uri, skin_refs, pkg)
                        skin_variants[skey] = None
                        if prims:
                            mi = writer.mesh(sname, prims)
                            writer.node(sname, mi, extras={"scct_kind": "library"})
                            skin_variants[skey] = sname
                    if skin_variants.get(skey):
                        entry["mesh"] = skin_variants[skey]
            # StaticMesh.UseVertexColor: the mesh's own authored colour stream replaces lighting (texture x colour) on
            # lit and bUnlit actors alike; the per-instance baked colours are unused. 32 Versus meshes: aqua_caustic01
            # (40/255 grey fading to 0 at the rim), light beams (Map17 PB_hangarlamp_BEAM, Prison beam04), sky and warp
            # backdrops. The variant is listed in authored_vc_meshes so the importer multiplies additive slots by it.
            # Only meshes with an additive section for now (opaque UseVertexColor meshes keep the baked / unlit path
            # until the opaque rule is checked against the game).
            authored = None
            if loaded is not None and loaded[1].props.get("UseVertexColor") and loaded[1].colors is not None                     and len(loaded[1].colors) == len(loaded[1].positions) and not rec.hidden                     and any(sec.material_ref and resolver.resolve(loaded[0], sec.material_ref).additive
                            for sec in loaded[1].sections):
                owner, sm = loaded
                authored = sm.colors
                h = hashlib.md5(authored.tobytes()).hexdigest()[:8]
                vkey = f"{key}|avc|{skin_sig}"
                if vkey not in color_variants:
                    vname = _safe(key) + f"__vc{h}" + (f"_sk{skin_id}" if skin_sig else "")
                    prims = _static_mesh_prims(sm, owner, resolver, writer, tex_dir, tex_uri,
                                               skin_refs if skin_sig else None, pkg)
                    prims = [tuple(pr) + (None, authored) for pr in prims]
                    color_variants[vkey] = None
                    if prims:
                        mi = writer.mesh(vname, prims)
                        writer.node(vname, mi, extras={"scct_kind": "library"})
                        color_variants[vkey] = vname
                        vertex_lit.append({"name": vname, "base": _safe(key)})
                        authored_vc.append(vname)
                if color_variants.get(vkey):
                    entry["mesh"] = color_variants[vkey]
                    entry["vertex_lit"] = True
                    entry["authored_vc"] = True
            # Actor.bUnlit: a fullbright variant; its baked vertex colours are not used by the game.
            if rec.extra.get("unlit") and loaded is not None and authored is None:
                owner, sm = loaded
                ukey = f"{key}|unlit|{skin_sig}"
                if ukey not in skin_variants:
                    uname = _safe(key) + "__ul" + (f"_sk{skin_id}" if skin_sig else "")
                    prims = _static_mesh_prims(sm, owner, resolver, writer, tex_dir, tex_uri,
                                               skin_refs if skin_sig else None, pkg, unlit=True)
                    skin_variants[ukey] = None
                    if prims:
                        mi = writer.mesh(uname, prims)
                        writer.node(uname, mi, extras={"scct_kind": "library"})
                        skin_variants[ukey] = uname
                if skin_variants.get(ukey):
                    entry["mesh"] = skin_variants[ukey]
            # Per-actor baked vertex lighting (StaticMeshInstance colour stream): a mesh variant with COLOR_0.
            smi_ref = rec.extra.get("smi")
            if smi_ref and loaded is not None and not rec.hidden and not rec.extra.get("unlit") and authored is None:
                colors = _read_color_stream(pkg, int(smi_ref))
                owner, sm = loaded
                if colors is not None and len(colors) == len(sm.positions):
                    h = hashlib.md5(colors.tobytes()).hexdigest()[:8]
                    vkey = f"{key}|{h}|{skin_sig}"
                    if vkey not in color_variants:
                        vname = _safe(key) + f"__vc{h}" + (f"_sk{skin_id}" if skin_sig else "")
                        prims = _static_mesh_prims(sm, owner, resolver, writer, tex_dir, tex_uri,
                                                   skin_refs if skin_sig else None, pkg)
                        prims = [tuple(pr) + (None, colors) for pr in prims]
                        if prims:
                            mi = writer.mesh(vname, prims)
                            writer.node(vname, mi, extras={"scct_kind": "library"})
                            color_variants[vkey] = vname
                            vertex_lit.append({"name": vname, "base": _safe(key)})
                        else:
                            color_variants[vkey] = None
                    if color_variants.get(vkey):
                        entry["mesh"] = color_variants[vkey]
                        entry["vertex_lit"] = True
                        # Brightest baked channel (0-255): all-black baked colours mean the mesh got no static light
                        # (BankD's skyline, ClubD's sky), where AmbientGlow / zone ambient stand in (importer).
                        entry["vc_max"] = int(colors[:, :3].max()) if len(colors) else 0
        if rec.kind == "light" and rec.extra.get("echelon_material_ref"):
            # ESpotShadow projected texture (EchelonLightingMaterial.ProjectedLightingTexture) for the importer's light
            # function over the frustum box (docs/LIGHTING_BALANCE.md "Projected lighting texture").
            eref = rec.extra.pop("echelon_material_ref")
            try:
                owner, e = resolver.pset.resolve(pkg, eref)
                tref = resolver._props(owner, e).get("ProjectedLightingTexture") if e is not None else None
                if isinstance(tref, int) and tref:
                    tres = resolver.resolve(owner, tref)
                    if not tres.skip and tres.texture is not None:
                        entry["echelon_texture_uri"] = _texture_png(tres.key, tres.texture, tex_dir, tex_uri)
            except Exception as ex:   # an unresolvable material leaves the plain spot
                entry["echelon_texture_error"] = str(ex)[:120]
        if rec.kind == "light" and "corona" in rec.extra:
            corona = rec.extra["corona"]
            ref = corona.pop("texture_ref", 0)
            res = resolver.resolve(pkg, ref) if ref else None
            corona["texture_uri"] = _ensure_texture(res, tex_dir, tex_uri) if res is not None and not res.skip else None
        elif rec.kind == "projector":
            # The projector stays a marker (runtime name match); its decal is a separate world-space mesh.
            prims, pkey = _projector_prims(rec, world_polys, pkg, resolver, writer, tex_dir, tex_uri)
            if prims:
                pname = _safe(f"PROJ_{rec.name}")
                writer.node(pname, writer.mesh(pname, prims), extras={"scct_kind": "library"})
                scene_actors.append({"name": pname, "class": "Projector", "kind": "staticmesh", "location": [0.0, 0.0, 0.0],
                                     "rotation": [0, 0, 0], "scale": [1.0, 1.0, 1.0], "tag": "", "hidden": False,
                                     "mesh": pname, "mesh_available": True, "projector_of": rec.name,
                                     "material": _safe(pkey), "projector_persp": bool(rec.extra.get("fov", 0))})
                stats["projectors"] = stats.get("projectors", 0) + 1
            if rec.extra.get("fov", 0) and rec.extra.get("proj_texture_ref"):
                # Perspective projectors (ClubD's pool caustics): the Faithful look is the modulating decal mesh above;
                # the Enhanced profile throws the texture through a spot light, so the importer needs the texture.
                pres = resolver.resolve(pkg, rec.extra["proj_texture_ref"])
                if not pres.skip and pres.texture is not None:
                    entry["proj_texture_uri"] = _texture_png(pres.key, pres.texture, tex_dir, tex_uri)
        if rec.extra.get("rain_material_ref"):
            # ERainVolume material (e.g. TXT_ORP.StandardRainMaterial: FinalBlend FB_Translucent over a
            # TexFriendlyPanner over GFX_Rain2): its texture, blend and panner for the client's rain sheets.
            rres = resolver.resolve(pkg, rec.extra["rain_material_ref"])
            if not rres.skip and rres.texture is not None:
                rec.extra["rain"]["texture_uri"] = _texture_png(rres.key, rres.texture, tex_dir, tex_uri)
                rec.extra["rain"]["additive"] = bool(rres.additive)
                rec.extra["rain"]["additive_gain"] = float(rres.additive_gain)
                rec.extra["rain"]["uv_anim"] = rres.uv_anim
        if rec.extra.get("patch_material_ref"):
            # ESBPatchActor (soft-body cloth: AquaE's sea, awnings): a static grid mesh standing in for the runtime
            # simulation; the actor's DrawScale scales it like any mesh actor.
            pres = resolver.resolve(pkg, rec.extra["patch_material_ref"])
            if not pres.skip:
                pname = _safe(f"PATCH_{rec.name}")
                glow = int(rec.extra["patch"].get("ambient_glow", 0))
                # UESoftBody::Render makes no lighting calls: soft bodies (DrawType 13) draw fullbright. AquaD's sea has
                # no light in reach and zone ambient 0, yet the game shows near-white crests (matched live only with
                # the light term at 1.0). The lit path below stays for reference.
                unlit = True
                prims = _patch_prims(rec.extra["patch"], pres, writer, tex_dir, tex_uri, unlit)
                if not unlit:
                    # No baked light (the game builds the patch at runtime); AmbientGlow adds to lighting like on static
                    # meshes (ambient_glow -> Custom Primitive Data 28 on M_SCCT_VertexLit, capped there), not as a
                    # vertex colour: baked colours go through the display-space gamma and left AquaE's sea near black.
                    col = np.zeros((len(prims[0][0]), 4), np.uint8)
                    col[:, 3] = 255
                    prims = [tuple(pr) + (None, col) for pr in prims]
                    entry["ambient_glow"] = min(glow, 255)
                writer.node(pname, writer.mesh(pname, prims), extras={"scct_kind": "library"})
                entry.update({"kind": "staticmesh", "mesh": pname, "mesh_available": True})
                if not unlit:
                    vertex_lit.append({"name": pname, "base": pname})
                    entry["vertex_lit"] = True
                stats["patches"] = stats.get("patches", 0) + 1
        entry.update({k: v for k, v in rec.extra.items() if k not in ("skin_refs", "proj_texture_ref", "rain_material_ref",
                                                                       "patch_material_ref")})
        scene_actors.append(entry)

    # Swap meshes: library nodes for the meshes gameplay objects change to at run time.
    stats["swap_meshes"] = 0
    for ref in extra_mesh_refs or []:
        if not isinstance(ref, int) or ref == 0:
            continue
        key = pset.import_path(pkg, ref)
        if not key or key in mesh_cache:
            continue
        mesh_cache[key] = None
        loaded = load_mesh(ActorRecord(name=key, cls="StaticMesh", kind="staticmesh", mesh_key=key, mesh_ref=ref))
        if loaded is None:
            continue
        owner, sm = loaded
        try:
            prims = _static_mesh_prims(sm, owner, resolver, writer, tex_dir, tex_uri)
            if prims:
                mesh_cache[key] = writer.mesh(_safe(key), prims)
                writer.node(_safe(key), mesh_cache[key], extras={"scct_kind": "library"})
                stats["swap_meshes"] += 1
        except Exception as ex:
            stats["missing_meshes"].append(f"{key}: {ex}")

    gltf_path = out_dir / f"{map_name}.gltf"
    writer.write(gltf_path)
    scene = {"map": map_name, "gltf": gltf_path.name, "source": str(map_path), "actors": scene_actors,
             "materials": list(writer.material_index.keys()), "additive_materials": _additive_list(writer), "modulate_materials": _modulate_list(writer),
             "material_flags": {_safe(k): v for k, v in writer.material_flags.items()},
             "lightmapped_materials": lightmapped, "vertex_lit_meshes": vertex_lit, "authored_vc_meshes": authored_vc,
             "emitters": emitters,
             "sky": ({"zone": sky_zone, "actor": sky_rec.name, "location": list(sky_rec.location),
                      "rotation": list(sky_rec.rotation)} if (sky_rec is not None and sky_zone is not None) else None),
             # LevelInfo post-process settings: glow_threshold is the native glow's brightpass (T = value / 255).
             "look": extract_look(pkg),
             "stats": {k: v for k, v in stats.items() if k != "missing_meshes"},
             "missing_meshes": sorted(set(stats["missing_meshes"]))[:200]}
    (out_dir / "scene.json").write_text(json.dumps(scene, indent=1), encoding="utf-8")
    return stats
