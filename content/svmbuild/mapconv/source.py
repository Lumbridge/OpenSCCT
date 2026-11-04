"""One original map opened for conversion: package, Level export, GE partition and every actor with typed props.

Actor categories (decided per actor, in Level.Actors order):

    level     Actors[0], the LevelInfo
    gameplay  every other actor the simulation can see; written to sim/gameplay.json
    render    decoration only the client draws (static mesh actors, plain lights, ambient sounds, projectors,
              anti-portals, rain volumes, soft-body cloth); written to render/ and, when they collide, to
              sim/collision.bin without an actor id
    editor    editor viewports (Camera) and the builder brush (Engine.Brush); not written
    skipped   actors that never exist in the PC game: bDeleteMe / bPendingDelete ghosts and PS2-only actors
              (Platform = PLF_PSX2_Only); not written, counted

An editor copy (Packages/MapsEd, the stock editor's working copy with its brushes) converts like the cooked copy the
editor saves next to it, which is what the game loads: the actors the cook leaves out of Level.Actors (CSG brushes and
static lights, see left_out_by_cook) are kept apart in `cooked_out` (category "editor", reason "cooked out"), and actor
properties are stored the way the cook stores them (see _as_cooked).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from functools import cached_property
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from ..sources.classdb import ClassDB
from ..sources.env import import_formats
from ..sources.typedprops import Notes, Ref, default_resolver, walk_refs

RF_HAS_STACK = 0x02000000
PLF_PSX2_ONLY = 1
LE_ESPOT_SHADOW = 22
COOKED_OUT = "cooked out"

EDITOR_CLASSES = ("Engine.Camera", "Engine.Brush")
RENDER_CLASSES = ("Engine.StaticMeshActor", "Engine.Light", "Engine.Spotlight", "Engine.SunLight",
                  "Engine.AmbientSound", "Engine.Projector", "Engine.DynamicProjector", "Engine.AntiPortalActor",
                  "Engine.ERainVolume", "SoftBody.ESBPatchActor", "SoftBody.ESBStripDoorActor", "SBase.SFogVolume")


@dataclass
class ActorInfo:
    position: int                 # index in Level.Actors
    ref: int                      # export index + 1
    name: str
    cls: str                      # Package.Class
    props: Dict[str, Any]         # decoded, typed (Ref / Name / int / float / ...)
    category: str = "gameplay"
    reason: str = ""
    error: str = ""


@dataclass
class MapSource:
    game: Path
    name: str
    db: ClassDB
    notes: Notes = field(default_factory=Notes)

    def __post_init__(self) -> None:
        sf = import_formats()
        from scctpkg.bsp import EDITOR  # noqa: PLC0415
        from scctpkg.package import Package  # noqa: PLC0415
        from scctpkg.packageset import PackageSet  # noqa: PLC0415
        from scctpkg.properties import skip_state_frame  # noqa: PLC0415

        from ..gamefiles import game_file, package_roots  # noqa: PLC0415

        self.path = game_file(Path(self.game), f"Packages/Maps/{self.name}.sdc")
        self.pkg = Package.load(self.path)
        self.editor_copy = self.pkg.layout == EDITOR
        self.pset = PackageSet(package_roots(Path(self.game)))
        self.pset.register(self.pkg)
        self._path_index: Dict[str, dict] = {}
        lv = sf.ge.level_export(self.pkg)
        self.level_export = lv
        self.header, self.partition = sf.ge.read_level(self.pkg.export_reader(lv), self.pkg.version)
        decoder = self.db.decoder
        self.actors: List[ActorInfo] = []
        self.by_ref: Dict[int, ActorInfo] = {}
        self.cooked_out: List[ActorInfo] = []
        from .localization import apply_text, read_text  # noqa: PLC0415

        text = read_text(self.game, self.name)
        for pos, ref in enumerate(self.header.actors):
            if ref <= 0:
                continue
            e = self.pkg.exports[ref - 1]
            cls = self.db.find(f"{e.class_package}.{e.class_name}") or f"{e.class_package}.{e.class_name}"
            info = ActorInfo(pos, ref, e.name, cls, {})
            rec = self.db.record(cls)
            if rec is None:
                info.error = f"class {cls} is not in the script packages"
                self.notes.add("unknown-class", f"{e.name} {cls}")
            else:
                r = self.pkg.export_reader(e)
                try:
                    skip_state_frame(r, e.flags)
                    info.props = decoder.tagged(r, self.pkg, rec["_field"])
                    # The build language's text over what the package stores (retail packages store French).
                    apply_text(info.props, text.get(e.name.lower()), lambda k, c=cls: self.db.prop(c, k))
                    if r.remaining():
                        self.notes.add("trailing-bytes", f"{e.name}: {r.remaining()}")
                except Exception as ex:  # noqa: BLE001 - one bad actor must not stop the map
                    info.error = f"{type(ex).__name__}: {ex}"
                    self.notes.add("decode-error", f"{e.name}: {info.error}")
            if self.editor_copy:
                if pos > 0 and self.left_out_by_cook(info):
                    info.category, info.reason = "editor", COOKED_OUT
                    self.cooked_out.append(info)
                    continue
                self._as_cooked(info)
            self.classify(info)
            self.actors.append(info)
            self.by_ref[ref] = info
        if not self.actors or not self.actors[0].cls.endswith("LevelInfo"):
            raise ValueError(f"{self.name}: Level.Actors[0] is not a LevelInfo")

    # ---- values --------------------------------------------------------------------------------

    def classify(self, info: ActorInfo) -> None:
        """Classify decoded actors, including newly introduced render-only classes absent from simulation codegen."""
        low = info.cls.lower()
        if info.position == 0:
            info.category = "level"
        elif low in {c.lower() for c in EDITOR_CLASSES}:
            info.category, info.reason = "editor", "editor only"
        elif self.value(info, "bDeleteMe") or self.value(info, "bPendingDelete"):
            info.category, info.reason = "skipped", "deleted in the editor"
        elif self.value(info, "Platform", 0) == PLF_PSX2_ONLY:
            info.category, info.reason = "skipped", "PS2 only"
        elif low in {c.lower() for c in RENDER_CLASSES}:
            info.category = "render"

    def left_out_by_cook(self, info: ActorInfo) -> bool:
        """True when the stock editor leaves the actor out of the cooked copy it saves next to the editor copy (actor
        list rebuild 0x10EE13B0, predicate 0x10EDD1E0; offsets mapped through the editor's class reflection):
        - an actor with a LightType (+0x2D1) is kept only when it matters at run time (0x10EDACF0):
          bApplyToInGameLighting (+0x2EC 0x800000), bDynamicLight (+0x2E8 0x20000), LightEffect (+0x2D2)
          LE_ESpotShadow, or both HeatRadius (+0x174) and HeatIntensity (+0x178) non-zero; the static lights are
          already baked into the lightmaps and vertex colours;
        - a brush is kept only when it is a volume (Engine.Brush 0x1181B048, Engine.Volume 0x1181B278)."""
        if int(self.value(info, "LightType", 0) or 0) != 0:
            return not (self.flag(info, "bApplyToInGameLighting") or self.flag(info, "bDynamicLight")
                        or int(self.value(info, "LightEffect", 0) or 0) == LE_ESPOT_SHADOW
                        or (float(self.value(info, "HeatRadius", 0.0) or 0.0) != 0.0
                            and float(self.value(info, "HeatIntensity", 0.0) or 0.0) != 0.0))
        return self.db.is_a(info.cls, "Engine.Brush") and not self.db.is_a(info.cls, "Engine.Volume")

    @staticmethod
    def _as_cooked(info: ActorInfo) -> None:
        """An editor copy's actor properties as the cooked copy stores them. AActor::Serialize (0x110B2490) writes the PC
        lighting instance under StaticMeshInstanceOnXBOX for the editor and under StaticMeshInstance for the cook, and
        the PS2 one (StaticMeshInstanceOnPSX2) only for the editor."""
        pc = info.props.pop("StaticMeshInstanceOnXBOX", None)
        info.props.pop("StaticMeshInstanceOnPSX2", None)
        if pc is not None:
            info.props["StaticMeshInstance"] = pc

    def value(self, info: ActorInfo, name: str, default: Any = None) -> Any:
        if name in info.props:
            return info.props[name]
        return self.db.default(info.cls, name, default)

    def flag(self, info: ActorInfo, name: str) -> bool:
        return bool(self.value(info, name, False))

    @property
    def level_info(self) -> ActorInfo:
        return self.actors[0]

    def of(self, *categories: str) -> List[ActorInfo]:
        return [a for a in self.actors if a.category in categories]

    @cached_property
    def unsaved_refs(self) -> set:
        """The cooked-out actors the cooked copy does not store at all. The cook only takes an actor out of Level.Actors;
        its save still writes the actor when a stored object refers to it (an IgnoredEchelonLights entry, say), and a
        cooked copy's every actor export is drawn (scctpkg level.extract_level), so those stay in the render."""
        pending = {a.ref: a for a in self.cooked_out}
        stack = list(self.actors)
        while stack and pending:
            for ref in walk_refs(stack.pop().props):
                if ref.ref in pending and self.own(ref):
                    stack.append(pending.pop(ref.ref))
        return set(pending)

    @cached_property
    def written_refs(self) -> set:
        return {a.ref for a in self.actors if a.category in ("level", "gameplay")}

    # ---- references ----------------------------------------------------------------------------

    def own(self, ref: Ref) -> bool:
        """True when the raw reference is relative to this map's package (not to a script package)."""
        return not ref.owner or ref.owner.lower() == self.pkg.path.stem.lower()

    def resolve(self, ref: Ref) -> Optional[dict]:
        """A reference stored in this map (or a class default), as SVMap JSON."""
        if ref.ref == 0:
            return None
        if not self.own(ref):
            return default_resolver(ref)
        if ref.ref > 0:
            e = self.pkg.exports[ref.ref - 1]
            info = self.by_ref.get(ref.ref)
            if info is not None:
                if info.category in ("level", "gameplay"):
                    return {"$actor": info.name}
                # A render or editor actor: it lives in the map's render content, not in the simulation.
                return {"$asset": f"{self.name}.{info.name}"}
            if e.flags & RF_HAS_STACK:
                self.notes.add("ref-to-actor-outside-level", e.name)
                return None
            if ref.is_class:
                return {"$class": ref.path} if ref.path.count(".") == 1 else {"$asset": ref.path}
            return {"$asset": ref.path}
        return default_resolver(ref)

    def ref_export(self, ref: Any) -> Tuple[Any, Any]:
        """(package, export) of a Ref (or a raw reference of this map), following imports into other packages.
        References from class defaults are relative to a script package and are found by their path."""
        if isinstance(ref, Ref) and not self.own(ref):
            return self.find_path(ref.path)
        raw = ref.ref if isinstance(ref, Ref) else int(ref or 0)
        if raw == 0:
            return None, None
        return self.pset.resolve(self.pkg, raw)

    def find_path(self, path: str) -> Tuple[Any, Any]:
        """(package, export) of a full object path Package.Group.Name."""
        if not path or "." not in path:
            return None, None
        top = path.split(".", 1)[0]
        pkg = self.pset.get(top)
        if pkg is None:
            return None, None
        index = self._path_index.get(top.lower())
        if index is None:
            cd = import_formats().classdefaults
            index = {cd.export_path(pkg, e).lower(): e for e in pkg.exports}
            self._path_index[top.lower()] = index
        e = index.get(path.lower())
        if e is None:
            e = pkg.find_export(path.rsplit(".", 1)[-1])
        return (pkg, e) if e is not None else (pkg, None)

    def discovered(self, ctx) -> dict:
        files: Dict[str, Optional[str]] = {}
        lookups: Dict[str, Optional[str]] = {}
        for key in sorted(self.pset.loaded):
            path = self.pset.files.get(key)
            if path is None or key == self.name.lower():
                lookups[key] = None if path is None else ctx.game_path(self.path)
                continue
            logical = ctx.game_path(path)
            lookups[key] = logical
            files[logical] = ctx.hasher.sha256(path)
        return {"files": files, "lookups": lookups}
