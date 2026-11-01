"""P6 audio step: the shared sound library (shared/audio/).

Every Sound export of Packages/Sounds/*.uax (wave data, streamed and multichannel sounds, random, switch and sequence
lists, gain and radius), the .mux channel maps and the sound references of the class defaults. The per-map banks
(.uas) are the audio-map step (steps/audio_map.py). See svmbuild/audio and docs/AUDIO.md.
"""

from __future__ import annotations

from pathlib import Path

from ..graph import Context, Step, StepOutput
from ..paths import REPO_ROOT
from ..sources import env

AUDIO_CODE = REPO_ROOT / "content" / "svmbuild" / "audio"


def audio_code() -> list[Path]:
    return sorted(AUDIO_CODE.glob("*.py")) + [REPO_ROOT / "content" / "svmbuild" / "canonical.py"]


class AudioStep(Step):
    name = "audio"
    version = 5
    deps = ()
    per_map = False
    needs_game = True
    description = "P6: every .uax Sound (RIFF waves, stream and list sounds), .mux maps, class sound refs (shared/audio/)."

    def extra_code(self) -> list[Path]:
        return env.code_files() + audio_code() + [REPO_ROOT / "content" / "svmbuild" / "sidearms.py", REPO_ROOT / "content" / "svmbuild" / "weapon_audio.py"] + [REPO_ROOT / "content" / "svmbuild" / "sources" / "classdb.py",
                                                  REPO_ROOT / "content" / "svmbuild" / "sources" / "typedprops.py"]

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        from ..audio.build import mux_files, package_files  # noqa: PLC0415

        out = {}
        for f in package_files(ctx.game) + mux_files(ctx.game) + env.script_package_files(ctx.game):
            out[ctx.game_path(f)] = ctx.hasher.sha256(f)
        from ..sidearms import SOURCE
        for f in sorted((SOURCE / "audio").glob("*.wav")):
            out["sidearms/audio/" + f.name] = ctx.hasher.sha256(f)
        out["sidearms/CREDITS.txt"] = ctx.hasher.sha256(SOURCE / "CREDITS.txt")
        from ..weapon_audio import SOURCE as WEAPONS
        for f in sorted(WEAPONS.glob('*')):
            if f.is_file(): out['weapons/'+f.name] = ctx.hasher.sha256(f)
        return out

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "shared" / "audio"

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        assert out_dir is not None
        from ..audio.build import build_library, package_files  # noqa: PLC0415
        from ..sources.classdb import class_db  # noqa: PLC0415

        if not package_files(ctx.game):
            raise ValueError(f"no .uax packages under {ctx.game / 'Packages' / 'Sounds'}")
        db = class_db(str(ctx.game)) if env.script_package_files(ctx.game) else None
        stats = build_library(ctx.game, out_dir, db)
        if stats["problems"]:
            raise ValueError("sounds that did not decode: " + "; ".join(stats["problems"][:10]))
        from ..sidearms import install_audio
        install_audio(out_dir)
        from ..weapon_audio import install
        stats['authored_weapons'] = install(out_dir)
        flat = {k: v for k, v in stats.items() if not isinstance(v, (dict, list))}
        flat["detail"] = stats
        return StepOutput(stats=flat)
