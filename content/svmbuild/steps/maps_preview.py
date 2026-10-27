"""maps-preview: the vendored scctpkg glTF export of one map into preview/<Map>/.

Not part of the shipped content: it proves the vendored reader works against an install and gives a quick look at
a map. Output is what `python -m scctpkg export` writes (<Map>.gltf, <Map>.bin, textures/, scene.json).
"""

from __future__ import annotations

from pathlib import Path

from .. import gamefiles
from ..graph import Context, Step, StepOutput
from ..paths import SCCTPKG_ROOT, import_scctpkg


def _scctpkg_files() -> list[Path]:
    root = SCCTPKG_ROOT / "scctpkg"
    return sorted(p for p in root.iterdir() if p.is_file() and p.suffix in (".py", ".json"))


class MapsPreviewStep(Step):
    name = "maps-preview"
    version = 1
    per_map = True
    needs_game = True
    description = "scctpkg glTF + scene.json export of one map, to prove the vendored reader (preview/<Map>/)."

    def options(self, ctx: Context, unit: str | None) -> dict:
        return {"map": unit, "includeBsp": True}

    def _map_file(self, ctx: Context, unit: str | None) -> Path:
        return gamefiles.game_file(ctx.game, f"Packages/Maps/{unit}.sdc")

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        map_file = self._map_file(ctx, unit)
        return {
            ctx.game_path(map_file): ctx.hasher.sha256(map_file),
            "repo:tools/scctpkg/scctpkg": ctx.hasher.tree(SCCTPKG_ROOT / "scctpkg", _scctpkg_files()),
        }

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "preview" / str(unit)

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        assert out_dir is not None and unit is not None
        import_scctpkg(require_class_defaults=True)
        from scctpkg.export_gltf import export_map  # noqa: PLC0415 - vendored, imported on demand
        from scctpkg.packageset import PackageSet  # noqa: PLC0415

        pset = PackageSet(gamefiles.package_roots(ctx.game))
        stats = export_map(self._map_file(ctx, unit), pset, out_dir.parent, include_bsp=True)
        # Packages the export opened (or looked for and did not find) decide its output beyond the map itself.
        files: dict[str, str | None] = {}
        lookups: dict[str, str | None] = {}
        for key in sorted(pset.loaded):
            path = pset.files.get(key)
            if path is None:
                lookups[key] = None
                continue
            logical = ctx.game_path(path)
            lookups[key] = logical
            files[logical] = ctx.hasher.sha256(path)
        out_files = [p for p in out_dir.rglob("*") if p.is_file()]
        summary = {
            "bspPolys": stats.get("bsp_polys", 0),
            "staticMeshes": stats.get("static_meshes", 0),
            "actors": stats.get("actors", 0),
            "missingMeshes": len(stats.get("missing_meshes", [])),
            "packagesOpened": sum(1 for v in lookups.values() if v is not None),
            "files": len(out_files),
            "bytes": sum(p.stat().st_size for p in out_files),
        }
        return StepOutput(discovered={"files": files, "lookups": lookups}, stats=summary)

    def discovered_ok(self, ctx: Context, unit: str | None, discovered: dict) -> bool:
        if not super().discovered_ok(ctx, unit, discovered):
            return False
        lookups = discovered.get("lookups", {})
        if not lookups:
            return True
        import_scctpkg()
        from scctpkg.packageset import PackageSet  # noqa: PLC0415

        index = PackageSet(gamefiles.package_roots(ctx.game)).files
        for key, logical in lookups.items():
            path = index.get(key)
            if (ctx.game_path(path) if path is not None else None) != logical:
                return False
        return True
