"""fx step: the gameplay effect classes and their assets -> shared/fx/ (see effects.py for the layout)."""

from __future__ import annotations

from pathlib import Path

from ..graph import Context, Step, StepOutput
from ..sources import env

PACKAGE_DIRS = (("Textures", "*.utx"), ("StaticMeshes", "*.usx"))


class FxStep(Step):
    name = "fx"
    version = 1
    deps = ()
    per_map = False
    needs_game = True
    description = "gameplay effects: effect classes, sub-emitters, their textures and meshes (shared/fx/)."
    code_files = ("content/svmbuild/fx/effects.py", "content/svmbuild/sources/classdb.py",
                  "content/svmbuild/sources/typedprops.py", "content/svmbuild/writers/glb.py",
                  "content/svmbuild/canonical.py")

    def extra_code(self) -> list[Path]:
        return env.code_files()

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        out = {ctx.game_path(f): ctx.hasher.sha256(f) for f in env.script_package_files(ctx.game)}
        for sub, pattern in PACKAGE_DIRS:
            for f in sorted((ctx.game / "Packages" / sub).glob(pattern)):
                out[ctx.game_path(f)] = ctx.hasher.sha256(f)
        return out

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "shared" / "fx"

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        from .effects import build_pack  # noqa: PLC0415

        assert out_dir is not None
        stats = build_pack(ctx.game, out_dir)
        return StepOutput(stats=stats)
