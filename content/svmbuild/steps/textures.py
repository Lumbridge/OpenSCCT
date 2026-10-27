"""P2 textures step (not implemented yet).

Textures with full mip chains, raw BCn to DDS (packs/textures/).
"""

from __future__ import annotations

from ..graph import Step


class TexturesStep(Step):
    name = "textures"
    version = 1
    deps = ()
    per_map = False
    stub = True
    needs_game = True
    description = "P2: textures with full mip chains, raw BCn to DDS (packs/textures/)."
