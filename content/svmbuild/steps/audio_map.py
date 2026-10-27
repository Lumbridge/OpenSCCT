"""audio-map step: one map's sound bank and sound references (svmap/<Map>/audio/).

The map's .uas bank (index checked, every Ogg page CRC checked, each stream written as an .ogg), the sound references
of every actor (also the client-only AmbientSound actors), the zones (ambience and I3DL2 reverb) and the table of
every sound the map can play (sound id -> file, volume, radius, pitch, loop). See svmbuild/audio and docs/AUDIO.md.

The maps step owns the map folder and its manifest.json. This step writes only audio/ and then puts its files and
the audio group hash into the manifest; it does so again when it restores its files from the cache, because a maps
rebuild or restore rewrites the manifest without them (and removes audio/ as unknown files).
"""

from __future__ import annotations

from pathlib import Path

from ..graph import Context, Step, StepOutput
from ..sources import env
from .audio import audio_code


class AudioMapStep(Step):
    name = "audio-map"
    version = 3
    deps = ("audio", "maps")
    per_map = True
    needs_game = True
    description = "One map's .uas bank (Ogg streams, CRCs), actor and zone sound refs, sound table (svmap/<Map>/audio/)."

    def extra_code(self) -> list[Path]:
        root = Path(__file__).resolve().parents[1]
        return env.code_files() + audio_code() + [root / "mapconv" / "source.py", root / "sources" / "classdb.py",
                                                  root / "sources" / "typedprops.py", root / "sources" / "modeltail.py"]

    def options(self, ctx: Context, unit: str | None) -> dict:
        return {"map": unit}

    def _map_dir(self, ctx: Context, unit: str | None) -> Path:
        return ctx.out / "svmap" / str(unit)

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        from ..audio.build import bank_file  # noqa: PLC0415

        out: dict[str, str | None] = {}
        from ..gamefiles import game_file  # noqa: PLC0415

        sdc = game_file(ctx.game, f"Packages/Maps/{unit}.sdc")
        out[ctx.game_path(sdc)] = ctx.hasher.sha256(sdc)
        bank = bank_file(ctx.game, str(unit))
        out[ctx.game_path(bank) if bank.is_file() else f"game:Packages/Sounds/{unit}.uas"] = (
            ctx.hasher.sha256(bank) if bank.is_file() else None)
        for f in env.script_package_files(ctx.game):
            out[ctx.game_path(f)] = ctx.hasher.sha256(f)
        return out

    def output_files(self, ctx: Context, unit: str | None) -> list[str] | None:
        from ..audio.build import bank_layout  # noqa: PLC0415

        base = f"svmap/{unit}/audio/"
        return [base + rel for rel in bank_layout(ctx.game, str(unit))]

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        from ..audio.build import build_map_audio, load_library, patch_manifest  # noqa: PLC0415
        from ..mapconv.source import MapSource  # noqa: PLC0415
        from ..sources.classdb import class_db  # noqa: PLC0415

        map_dir = self._map_dir(ctx, unit)
        if not (map_dir / "manifest.json").exists():
            return StepOutput(stats={"reason": "map not built"}, cacheable=False, status="skipped")
        shared = ctx.out / "shared" / "audio"
        library = load_library(shared)
        src = MapSource(ctx.game, str(unit), class_db(str(ctx.game)))
        stats = build_map_audio(ctx.game, str(unit), map_dir / "audio", library, src)
        files = self._listing(ctx, map_dir)
        patch_manifest(map_dir, files, ctx.hasher.sha256(shared / "audio.json"))
        discovered = src.discovered(ctx)
        discovered["audio"] = files
        flat = {k: v for k, v in stats.items() if not isinstance(v, (dict, list))}
        return StepOutput(discovered=discovered, stats=flat)

    def discovered_ok(self, ctx: Context, unit: str | None, discovered: dict) -> bool:
        """Before a cache hit restores audio/: the inputs the map conversion opened must be unchanged, and the
        manifest gets the audio files back (the restore that follows puts the files themselves back)."""
        if not super().discovered_ok(ctx, unit, discovered):
            return False
        map_dir = self._map_dir(ctx, unit)
        if not (map_dir / "manifest.json").exists() or "audio" not in discovered:
            return False
        from ..audio.build import patch_manifest  # noqa: PLC0415

        shared = ctx.out / "shared" / "audio" / "audio.json"
        patch_manifest(map_dir, discovered["audio"], ctx.hasher.sha256(shared) if shared.is_file() else None)
        return True

    @staticmethod
    def _listing(ctx: Context, map_dir: Path) -> dict[str, dict]:
        root = map_dir / "audio"
        out = {}
        for p in sorted(root.rglob("*")):
            if p.is_file():
                out[p.relative_to(root).as_posix()] = {"sha256": ctx.hasher.sha256(p), "size": p.stat().st_size}
        return out
