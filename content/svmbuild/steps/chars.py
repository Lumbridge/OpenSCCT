"""P5 chars step: characters, first-person weapons and map animation props as GLB skins plus animation files
(shared/chars/<Package>/<Mesh>.glb, <Animation>.anim.json, textures/). See svmbuild/charconv.py.

Sets: SPerso ATT_01 (spy) and DEF_01 (merc), every SWeaponFirst mesh except the PS2 variants, the SGoggle beams
(goggle glow), and every skeletal mesh a map places (charconv.MAP_PROP_SETS: SAnimatedMesh props such as SteeE's
crane, Aqua's palms and FactD's grilles, and the skeletal fish). Materials a class swaps into Skins at run time (the merc rifle's rate-of-fire screens)
are exported with them, and the textures of the normal-vision wet skins (charconv.WET_SKINS). Then OpenSCCT's own cosmetic camos are painted over the body and gun textures (svmbuild/cosmetics.py).
"""

from __future__ import annotations

from pathlib import Path

from ..graph import Context, Step, StepOutput
from ..sources import env


class CharsStep(Step):
    name = "chars"
    version = 8
    deps = ()
    per_map = False
    background = True   # no other step reads shared/chars; runs beside the maps
    needs_game = True
    description = "P5: characters, first-person weapons and map anim props: GLB skins + anim files (shared/chars/)."
    code_files = ("content/svmbuild/charconv.py", "content/svmbuild/sources/animations.py",
                  "content/svmbuild/sources/classdb.py", "content/svmbuild/sources/typedprops.py",
                  "content/svmbuild/writers/glb.py", "content/svmbuild/canonical.py", "content/svmbuild/cosmetics.py",
                  "content/svmbuild/skeleton.py", "content/svmbuild/gunskin.py", "content/svmbuild/sidearms.py", "content/svmbuild/sidearm_models.py", "content/svmbuild/pistol_arms.py")

    def extra_code(self) -> list[Path]:
        return env.code_files()

    def options(self, ctx: Context, unit: str | None) -> dict:
        from ..charconv import DEFAULT_SETS  # noqa: PLC0415

        return {"sets": [[p, list(m)] for p, m in DEFAULT_SETS]}

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        from ..charconv import DEFAULT_SETS, MAP_PROP_SETS, import_packages  # noqa: PLC0415

        out = {}
        for p, _ in DEFAULT_SETS:
            f = ctx.game / "Packages" / "Animations" / f"{p}.ukx"
            out[ctx.game_path(f)] = ctx.hasher.sha256(f) if f.is_file() else None   # an optional map's props
        for f in env.script_package_files(ctx.game):
            out[ctx.game_path(f)] = ctx.hasher.sha256(f)
        # Skins and notify sounds come from other packages; the textures folder decides the PNGs. The map props'
        # skins are in the texture packages their .ukx imports.
        skins = {"SPersoTextures", "SWeaponTex", "SGoggleTextures"}
        for p, _ in MAP_PROP_SETS:
            skins.update(import_packages(ctx.game, p))
        for name in sorted(skins):
            tex = ctx.game / "Packages" / "Textures" / f"{name}.utx"
            if tex.is_file():
                out[ctx.game_path(tex)] = ctx.hasher.sha256(tex)
        from ..sidearms import source_files, SOURCE
        for f in source_files(): out["sidearms/" + f.relative_to(SOURCE).as_posix()] = ctx.hasher.sha256(f)
        return out

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "shared" / "chars"

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        assert out_dir is not None
        from ..charconv import export_all  # noqa: PLC0415
        from ..sources.classdb import class_db  # noqa: PLC0415

        stats = export_all(ctx.game, out_dir, class_db(str(ctx.game)), jobs=ctx.jobs)
        failed = [f for k, v in stats.items() if isinstance(v, dict) for f in v.get("failed", [])]
        if failed:
            raise ValueError("chars failed: " + "; ".join(failed[:10]))
        from ..cosmetics import build_all  # noqa: PLC0415

        from ..sidearms import install_models
        cosmetics = build_all(out_dir)
        sidearms = install_models(out_dir)
        summary = {"meshes": sum(len(v["meshes"]) for k, v in stats.items() if not k.startswith("_")),
                   "animations": sum(len(v["animations"]) for k, v in stats.items() if not k.startswith("_")),
                   "cosmetics": cosmetics, "sidearms": sidearms, "detail": stats}
        return StepOutput(stats=summary)
