"""maps-catalog: what the menus list about the maps besides each SVMap folder (docs/MAPS.md "The map catalog").

Writes shared/maps/catalog.json and shared/maps/images/<Map>/{menu,loading-spy,loading-merc,briefing}.png from
the install: the original's host map list order (Packages/Maps), the tutorial maps per side, Reloaded's playlists,
map names and variant words (System/Reloaded.Core.dll), and each map's pictures (Packages/Textures/<Map>-i.utc).
Without Reloaded.Core.dll the catalog has no playlists and no Reloaded names (the menus then use the map names).
"""

from __future__ import annotations

from pathlib import Path

from .. import canonical
from ..graph import Context, Step, StepOutput
from ..paths import SCCTPKG_ROOT

FORMAT_VERSION = 1


def _scctpkg_files() -> list[Path]:
    root = SCCTPKG_ROOT / "scctpkg"
    return sorted(p for p in root.iterdir() if p.is_file() and p.suffix in (".py", ".json"))


class MapsCatalogStep(Step):
    name = "maps-catalog"
    version = 1
    per_map = False
    needs_game = True
    description = "Map list order, Reloaded playlists and names, map pictures (shared/maps/)."
    code_files = ("content/svmbuild/mapcatalog.py", "content/svmbuild/canonical.py")

    def extra_code(self) -> list[Path]:
        return _scctpkg_files()

    def _map_files(self, ctx: Context) -> list[Path]:
        folder = ctx.game / "Packages" / "Maps"
        return sorted((p for p in folder.iterdir() if p.is_file() and p.suffix.lower().startswith(".sd")),
                      key=lambda p: p.name.upper()) if folder.is_dir() else []

    def _info_packages(self, ctx: Context) -> list[Path]:
        folder = ctx.game / "Packages" / "Textures"
        return sorted(folder.glob("*-i.utc"), key=lambda p: p.name.upper()) if folder.is_dir() else []

    def _reloaded(self, ctx: Context) -> Path:
        return ctx.game / "System" / "Reloaded.Core.dll"

    def options(self, ctx: Context, unit: str | None) -> dict:
        return {"formatVersion": FORMAT_VERSION}

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        out: dict[str, str | None] = {}
        # The map list is the file names, not their contents.
        listing = "\n".join(p.name for p in self._map_files(ctx))
        out["listing:Packages/Maps"] = canonical.sha256_hex(listing.encode("utf-8"))
        for p in self._info_packages(ctx):
            out[ctx.game_path(p)] = ctx.hasher.sha256(p)
        dll = self._reloaded(ctx)
        out["game:System/Reloaded.Core.dll"] = ctx.hasher.sha256(dll) if dll.is_file() else None
        return out

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "shared" / "maps"

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        assert out_dir is not None
        from .. import mapcatalog  # noqa: PLC0415

        files = [p.name for p in self._map_files(ctx)]
        doc: dict = {
            "format": "svm-map-catalog",
            "formatVersion": FORMAT_VERSION,
            "originalOrder": mapcatalog.original_map_order(files),
            "training": mapcatalog.training_maps(files),
            "menuMaps": [m for m in mapcatalog.MENU_MAPS if any(f.rpartition(".")[0].lower() == m.lower() for f in files)],
            "playlists": [],
            "names": {},
            "variantWords": [],
            "images": {},
        }
        warnings: list[str] = []
        dll = self._reloaded(ctx)
        if dll.is_file():
            defaults = mapcatalog.read_reloaded_defaults(dll.read_bytes())
            doc["playlists"] = defaults.playlists
            doc["names"] = dict(sorted(defaults.names.items()))
            doc["variantWords"] = defaults.variant_words
        else:
            warnings.append("System/Reloaded.Core.dll not found: no playlists or Reloaded names")
        for pkg in self._info_packages(ctx):
            code = pkg.name[: -len("-i.utc")]
            try:
                written = mapcatalog.export_images(pkg, out_dir / "images" / code)
            except Exception as e:  # noqa: BLE001 - one bad package must not stop the catalog
                warnings.append(f"{pkg.name}: {e}")
                continue
            if written:
                doc["images"][code] = {k: f"images/{code}/{v['file']}" for k, v in sorted(written.items())}
        doc["images"] = dict(sorted(doc["images"].items(), key=lambda kv: kv[0].upper()))
        (out_dir / "catalog.json").write_bytes(canonical.dump_file(doc, ascii_keys=False))
        stats = {"maps": len(doc["originalOrder"]), "playlists": len(doc["playlists"]), "names": len(doc["names"]),
                 "images": sum(len(v) for v in doc["images"].values()), "warnings": warnings}
        return StepOutput(stats=stats)

