"""BSP render sections and baked lightmaps of SCCT maps (editor and cooked copies).

The sections and lightmap textures are read by their exact layout (bsp.read_model_tail; see docs/LIGHTMAPS.md):

    Sections: compact n; each {compact NumVertices; NumVertices x FBspVertex (40 bytes:
        f32x3 Position, f32x3 Normal, f32 U, V, f32 LightU, LightV); i32, objref Material, i32,
        u32 PolyFlags, i32 iLightMapTexture}
    LightMapTextures: compact n; each {objref, compact m, m x i32 lightmap ids (ascending), u64, i32,
        TLazyArray<u8> Data (W*H*4 BGRA), TLazyArray<u8> (empty), u8 Format (5 = RGBA8), i32 W, i32 H, i32}

Node tails (the last 20 bytes of FBspNode) are i32 iLeaf[2], i32 iSection, i32 iFirstVertex,
i32 iLightMap. A node's lightmap id maps to a texture through the id lists, and the node's
polygon vertices are Sections[iSection].Vertices[iFirstVertex : iFirstVertex + NumVertices]
(same order as the node's vertex pool), whose LightU/LightV address that texture directly.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional

import numpy as np

from .bsp import EDITOR, read_model_geometry, read_model_tail
from .package import Export, Package, PackageError

TEXF_RGBA8 = 5


@dataclass
class BspSection:
    vertices: np.ndarray        # (n, 10) float32: pos xyz, normal xyz, u, v, lu, lv
    lightmap_texture: int       # iLightMapTexture (-1 none)


@dataclass
class LightMapTexture:
    ids: List[int]
    width: int
    height: int
    data: bytes                 # BGRA8


@dataclass
class BspLighting:
    node_section: np.ndarray     # (nodes,) i32
    node_first_vertex: np.ndarray
    node_lightmap: np.ndarray    # lightmap id per node (-1 none)
    sections: List[BspSection]
    textures: List[LightMapTexture]
    id_to_texture: Dict[int, int] = field(default_factory=dict)

    def texture_for_node(self, node_index: int) -> int:
        lm = int(self.node_lightmap[node_index])
        return self.id_to_texture.get(lm, -1) if lm >= 0 else -1

    def node_vertices(self, node_index: int, num_vertices: int) -> Optional[np.ndarray]:
        s = int(self.node_section[node_index])
        f = int(self.node_first_vertex[node_index])
        if s < 0 or s >= len(self.sections):
            return None
        v = self.sections[s].vertices
        if f < 0 or f + num_vertices > len(v):
            return None
        return v[f:f + num_vertices]


def read_bsp_lighting(pkg: Package, e: Export) -> BspLighting:
    editor = pkg.layout == EDITOR
    model, r = read_model_geometry(pkg, e, editor)
    tail = read_model_tail(r, editor)
    if r.remaining():
        raise PackageError(f"{e.name}: {r.remaining()} bytes after the model")
    node_section = np.array([n.isection for n in model.nodes], np.int32)
    node_first = np.array([n.ifirstvertex for n in model.nodes], np.int32)
    node_lm = np.array([n.ilightmap for n in model.nodes], np.int32)
    sections = [BspSection(s.vertices, s.lightmap_texture) for s in tail.sections]
    textures: List[LightMapTexture] = []
    id_to_tex: Dict[int, int] = {}
    for t, rec in enumerate(tail.textures):
        if rec.format != TEXF_RGBA8 or len(rec.data) != rec.width * rec.height * 4:
            raise PackageError(f"lightmap texture {t}: format {rec.format}, {len(rec.data)} bytes for "
                               f"{rec.width} x {rec.height}")
        textures.append(LightMapTexture(rec.ids, rec.width, rec.height, rec.data))
        for i in rec.ids:
            id_to_tex[i] = t
    return BspLighting(node_section, node_first, node_lm, sections, textures, id_to_tex)


def lightmap_image(tex: LightMapTexture):
    """BGRA8 lightmap as a PIL RGB image (UE2 lightmaps are half-intensity: the material scales them)."""
    from PIL import Image
    arr = np.frombuffer(tex.data, dtype=np.uint8).reshape(tex.height, tex.width, 4)
    rgb = arr[:, :, [2, 1, 0]]
    return Image.fromarray(np.ascontiguousarray(rgb), "RGB")
