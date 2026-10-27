"""Pipeline steps in registration order (also the tie-break order of the build plan)."""

from __future__ import annotations

from ..fx.step import FxStep
from ..graph import Step
from .audio import AudioStep
from .audio_map import AudioMapStep
from .chars import CharsStep
from .classes import ClassesStep
from .maps import MapsStep
from .maps_catalog import MapsCatalogStep
from .maps_preview import MapsPreviewStep
from .meshes import MeshesStep
from .renderpack import RenderPackStep
from .testbox import TestBoxStep
from .textures import TexturesStep
from .ui import UiStep
from .validate import ValidateStep


def all_steps() -> list[Step]:
    return [ClassesStep(), TexturesStep(), MeshesStep(), CharsStep(), AudioStep(), UiStep(), MapsStep(),
            ValidateStep(), MapsPreviewStep(), MapsCatalogStep(), TestBoxStep(), RenderPackStep(),
            AudioMapStep(), FxStep()]
