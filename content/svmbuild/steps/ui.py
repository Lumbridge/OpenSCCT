"""P7 ui step: Magma pages, atlases, .mft fonts and Magma strings to shared/ui/ (svmbuild/uiconv, docs/HUD.md).

Reads Menus/{Hud,Options,common}.mgm, Menus/Fonts/*.mft, Packages/Textures/{Hud,Options,common}_mgm.utx and
SHudTextures.utx, System/_PC_/Magma*.int, and the stock key layouts System/Default_kbd_{0,1}.ini (copied to keys/).
Missing optional files are listed in the manifest; a missing Hud.mgm fails the step, since the HUD cannot draw
without it.
"""

from __future__ import annotations

from pathlib import Path

from ..graph import Context, Step, StepOutput
from ..sources import env


class UiStep(Step):
    name = "ui"
    version = 4
    deps = ()
    per_map = False
    needs_game = True
    description = "P7: Magma pages, atlases, .mft fonts and strings (shared/ui/)."
    code_files = ("content/svmbuild/uiconv/__init__.py", "content/svmbuild/uiconv/mgm.py",
                  "content/svmbuild/uiconv/mft.py", "content/svmbuild/uiconv/strings.py",
                  "content/svmbuild/uiconv/export.py", "content/svmbuild/canonical.py")

    def extra_code(self) -> list[Path]:
        return env.code_files()

    def options(self, ctx: Context, unit: str | None) -> dict:
        from ..uiconv import export  # noqa: PLC0415

        return {"packages": list(export.PACKAGES), "shud": list(export.SHUD_TEXTURES),
                "strings": list(export.STRING_FILES), "keys": list(export.KEY_FILES), "language": env.language()}

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        from ..uiconv.export import Sources  # noqa: PLC0415

        out: dict[str, str | None] = {}
        for f in Sources(Path(ctx.game)).all_inputs():
            out[ctx.game_path(f)] = ctx.hasher.sha256(f) if f.is_file() else None
        return out

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "shared" / "ui"

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        assert out_dir is not None
        from ..uiconv.export import export_ui  # noqa: PLC0415

        report = export_ui(ctx.game, out_dir)
        if "Hud" not in report.packages:
            raise FileNotFoundError("Menus/Hud.mgm not found: the HUD pages cannot be converted")
        stats = {"packages": {k: v["areas"] for k, v in report.packages.items()},
                 "fonts": {k: v["glyphs"] for k, v in report.fonts.items()},
                 "textures": sum(len(v) for v in report.textures.values()),
                 "missing": report.missing, "warnings": report.warnings}
        return StepOutput(stats=stats)
