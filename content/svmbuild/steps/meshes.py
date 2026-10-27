"""P3 meshes step (not implemented yet).

Static meshes and their decoded collision tails, per-poly and hulls (packs/meshes/).
"""

from __future__ import annotations

from ..graph import Step


class MeshesStep(Step):
    name = "meshes"
    version = 1
    deps = ("textures",)
    per_map = False
    stub = True
    needs_game = True
    description = "P3: static meshes and their decoded collision tails, per-poly and hulls (packs/meshes/)."
