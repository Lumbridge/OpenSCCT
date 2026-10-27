"""P1 classes step: exact class defaults of the game's script packages -> shared/classes.json.

Every class of Core, Engine, SBase, GUI, IpDrv and SoftBody (System/_PC_/*.u), read by scctformats.classdefaults
with typed values (svmbuild.sources.classdb): what each class stores (`stored`), the defaults merged down the super
chain with the English .int text applied (`defaults`, what the class default object holds), and its own property
declarations. Enums and structs are listed too, so a reader can type any value.

Layout (svm.classes/1):

    {"format": "svm.classes/1", "source": {"packages": {"Engine": "<sha256>", ...}},
     "classes": {"SBase.SFlag": {"super": "SBase.SObjectiveTrigger", "within": ..., "config": ..., "flags": "0x...",
                                 "stored": {...}, "defaults": {...}, "properties": [{"name", "type", ...}]}},
     "enums": {"Engine.Actor.EPhysics": ["PHYS_None", ...]}, "structs": {"Core.Object.Vector": {...}},
     "stats": {...}}

Values use the SVMap property JSON (spec/svmap/1.1/README.md). Shared by every converted map (manifest `requires`).
"""

from __future__ import annotations

from pathlib import Path

from .. import canonical
from ..graph import Context, Step, StepOutput
from ..sources import env

OUTPUT = "shared/classes.json"
FORMAT = "svm.classes/1"


def build_document(game: Path, hasher=None) -> tuple[dict, dict]:
    from ..sources.classdb import ClassDB  # noqa: PLC0415
    from ..sources.typedprops import Notes  # noqa: PLC0415

    db = ClassDB(game)
    notes = Notes()
    classes = {}
    for path in sorted(db.classes):
        rec = db.classes[path]
        entry = {
            "super": rec.get("super"),
            "within": rec.get("within"),
            "config": rec.get("config") if rec.get("config") not in (None, "None") else None,
            "flags": rec.get("flags"),
            "stored": db.stored_json(path, notes=notes),
            "defaults": db.defaults_json(path, notes=notes),
            "properties": db.own_properties(path),
        }
        if rec.get("shadows"):
            entry["shadows"] = dict(rec["shadows"])
        if "error" in rec:
            entry["error"] = rec["error"]
        classes[path] = entry
    enums, structs = db.enums_and_structs()
    stats = {"classes": len(classes), "errors": len(db.errors), "tags": db.decoder.stats.tags,
             "unknown": db.decoder.stats.unknown, "sizeMismatch": db.decoder.stats.size_mismatch,
             "typeMismatch": db.decoder.stats.type_mismatch, "enums": len(enums), "structs": len(structs),
             "notes": dict(sorted(notes.counts.items()))}
    sources = {}
    if hasher is not None:
        for f in env.script_package_files(game):
            sources[f.name] = hasher.sha256(f)
    doc = {"format": FORMAT, "source": {"files": sources, "packages": list(db.packages)}, "classes": classes,
           "enums": enums, "structs": structs, "stats": stats}
    return doc, stats


class ClassesStep(Step):
    name = "classes"
    version = 3
    deps = ()
    per_map = False
    needs_game = True
    description = "P1: exact class defaults of System/_PC_/*.u with typed values (shared/classes.json)."
    code_files = ("content/svmbuild/sources/classdb.py", "content/svmbuild/sources/typedprops.py",
                  "content/svmbuild/sources/env.py", "content/svmbuild/canonical.py")

    def extra_code(self) -> list[Path]:
        return env.code_files()

    def options(self, ctx: Context, unit: str | None) -> dict:
        return {"language": env.language()}

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        return {ctx.game_path(f): ctx.hasher.sha256(f) for f in env.script_package_files(ctx.game)}

    def output_files(self, ctx: Context, unit: str | None) -> list[str] | None:
        return [OUTPUT]

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        doc, stats = build_document(ctx.game, ctx.hasher)
        if stats["errors"] or stats["unknown"] or stats["sizeMismatch"]:
            raise ValueError(f"class defaults did not decode cleanly: {stats}")
        target = ctx.out / OUTPUT
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(canonical.dump_file(doc))
        # scctpkg's class defaults for the map steps after this one, when the builder has none (an install).
        from ..paths import SCCTPKG_ROOT  # noqa: PLC0415
        from ..sources import classdefaults_dump  # noqa: PLC0415
        classdefaults_dump.ensure(SCCTPKG_ROOT / "scctpkg", ctx.out)
        flat = {k: v for k, v in stats.items() if not isinstance(v, dict)}
        flat["bytes"] = target.stat().st_size
        return StepOutput(stats=flat)


def load_classes(out: Path) -> dict:
    """The shared classes document of a content root."""
    return canonical.loads((Path(out) / OUTPUT).read_bytes())
