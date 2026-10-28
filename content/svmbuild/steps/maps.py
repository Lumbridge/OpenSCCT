"""P4 maps step: one original map (.sdc) to an SVMap 1.1 folder (svmap/<Map>/).

sim/gameplay.json (every gameplay actor with its typed property bag, map settings, animation notifies, tag index,
event edges, mission tree), sim/classes.json (class defaults of the classes used), sim/collision.bin (world BSP,
brushes, static meshes, movers, glass, gameplay objects, cylinders, zones), sim/traversal.json (GEs), render/
(world.glb, materials, lighting, fx, textures) and ui/mapinfo.json. See svmbuild/mapconv and spec/svmap/1.1.
"""

from __future__ import annotations

from pathlib import Path

from .. import gamefiles
from ..graph import Context, Step, StepOutput
from ..paths import REPO_ROOT
from ..sources import env

CONTENT = REPO_ROOT / "content" / "svmbuild"


def _own_code() -> list[Path]:
    files = []
    for sub in ("mapconv", "sources", "writers"):
        files += sorted((CONTENT / sub).glob("*.py"))
    files += [CONTENT / "svmap.py", CONTENT / "canonical.py", CONTENT / "charconv.py"]
    return files


class MapsStep(Step):
    name = "maps"
    version = 5
    deps = ("classes",)
    # The map folder holds audio/, which a rebuild or restore of this step removes: audio-map puts it back.
    followers = ("audio-map",)
    per_map = True
    needs_game = True
    description = "P4: one original map to SVMap 1.1: gameplay, collision, GEs, render, ui (svmap/<Map>/)."

    def extra_code(self) -> list[Path]:
        return env.code_files() + _own_code()

    def options(self, ctx: Context, unit: str | None) -> dict:
        return {"map": unit, "render": not ctx.options.get("sim_only", False), "language": env.language()}

    def _map_file(self, ctx: Context, unit: str | None) -> Path:
        return gamefiles.game_file(ctx.game, f"Packages/Maps/{unit}.sdc")

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        out = {ctx.game_path(self._map_file(ctx, unit)): ctx.hasher.sha256(self._map_file(ctx, unit))}
        for f in env.script_package_files(ctx.game):
            out[ctx.game_path(f)] = ctx.hasher.sha256(f)
        # A downloaded map's own text (names, zone tips) is in the addons' System/_PC_.
        for f in gamefiles.addon_text_files(str(unit)):
            out[ctx.game_path(f)] = ctx.hasher.sha256(f)
        return out

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "svmap" / str(unit)

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        assert out_dir is not None and unit is not None
        from ..mapconv.build import build_map  # noqa: PLC0415
        from ..sources.classdb import class_db  # noqa: PLC0415

        db = class_db(str(ctx.game))
        pack = ctx.out / "shared" / "classes.json"
        stats = build_map(ctx.game, unit, out_dir, db, map_sha256=ctx.hasher.sha256(self._map_file(ctx, unit)),
                          classes_pack=ctx.hasher.sha256(pack) if pack.is_file() else None,
                          render=self.options(ctx, unit)["render"], ctx=ctx)
        discovered = stats.pop("discovered", {})
        summary = {
            "actors": stats["actors"]["actors"],
            "colliders": stats["collision"]["colliders"],
            "tris": stats["collision"]["triangles"],
            "ges": stats["traversal"]["ges"],
            "edges": stats["actors"]["eventEdges"],
            "simMB": round(stats["sizes"].get("sim", 0) / 1e6, 2),
            "renderMB": round(stats["sizes"].get("render", 0) / 1e6, 2),
            "notes": sum(stats["notes"].values()),
            "detail": stats,
        }
        return StepOutput(discovered=discovered, stats=summary)

    def discovered_ok(self, ctx: Context, unit: str | None, discovered: dict) -> bool:
        if not super().discovered_ok(ctx, unit, discovered):
            return False
        lookups = discovered.get("lookups", {})
        if not lookups:
            return True
        env.import_formats()
        from scctpkg.packageset import PackageSet  # noqa: PLC0415

        index = PackageSet(gamefiles.package_roots(ctx.game)).files
        for key, logical in lookups.items():
            if key == str(unit).lower():
                continue
            path = index.get(key)
            if (ctx.game_path(path) if path is not None else None) != logical:
                return False
        return True
