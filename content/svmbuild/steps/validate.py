"""P8 validate step: checks a built SVMap (svmap/<Map>/) against spec/svmap.

Checks: the JSON Schemas of the version each file declares; actor references; the derived indexes (tag index,
event edges, mission tree, GE activity); collision.bin structure and its actor names; the per-map class table;
world.glb and the render JSON files; the manifest digests and group hashes. Counts of the map are reported.

With --check-stable the map is converted a second time into a scratch folder and every file must be byte-identical
to the first build (hash stability).
"""

from __future__ import annotations

import shutil
from pathlib import Path

from ..graph import Context, Step, StepOutput


def map_counts(root: Path) -> dict:
    from .. import canonical  # noqa: PLC0415
    from ..writers.collision import KINDS, read_collision  # noqa: PLC0415

    g = canonical.loads((root / "sim" / "gameplay.json").read_bytes())
    t = canonical.loads((root / "sim" / "traversal.json").read_bytes())
    d = read_collision((root / "sim" / "collision.bin").read_bytes())
    kinds: dict = {}
    for c in d.colliders:
        kinds[KINDS[c.kind]] = kinds.get(KINDS[c.kind], 0) + 1
    return {"actors": 1 + len(g["actors"]), "eventEdges": len(g["eventEdges"]), "colliders": len(d.colliders),
            "colliderKinds": kinds, "triangles": len(d.tris), "cylinders": len(d.cylinders), "zones": len(d.zones),
            "ges": len(t["ges"])}


class ValidateStep(Step):
    name = "validate"
    version = 2
    deps = ("maps",)
    per_map = True
    needs_game = True
    description = "P8: validate svmap/<Map>/ (schemas, references, derived indexes, collision, hashes)."
    code_files = ("content/svmbuild/validation.py", "content/svmbuild/canonical.py", "content/svmbuild/svmap.py",
                  "content/svmbuild/writers/collision.py", "content/svmbuild/writers/glb.py")

    def options(self, ctx: Context, unit: str | None) -> dict:
        return {"map": unit, "checkStable": bool(ctx.options.get("check_stable"))}

    def _map_dir(self, ctx: Context, unit: str | None) -> Path:
        return ctx.out / "svmap" / str(unit)

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        root = self._map_dir(ctx, unit)
        if not (root / "manifest.json").exists():
            return StepOutput(stats={"reason": "not built"}, cacheable=False, status="skipped")
        from ..validation import validate_svmap  # noqa: PLC0415

        errors = validate_svmap(root)
        if errors:
            raise ValueError(f"{len(errors)} problem(s):\n" + "\n".join(errors[:60]))
        stats = map_counts(root)
        if ctx.options.get("check_stable"):
            stats["stable"] = self._check_stable(ctx, unit, root)
        flat = {k: v for k, v in stats.items() if not isinstance(v, dict)}
        flat["detail"] = stats
        return StepOutput(stats=flat)

    def _check_stable(self, ctx: Context, unit: str, root: Path) -> bool:
        from .. import canonical  # noqa: PLC0415
        from ..mapconv.build import build_map  # noqa: PLC0415
        from ..sources.classdb import class_db  # noqa: PLC0415

        manifest = canonical.loads((root / "manifest.json").read_bytes())
        scratch = ctx.out / "tmp" / "stable" / str(unit)
        if scratch.exists():
            shutil.rmtree(scratch)
        scratch.mkdir(parents=True)
        try:
            pack = ctx.out / "shared" / "classes.json"
            render = any(k.startswith("render/") for k in manifest["files"])
            build_map(ctx.game, unit, scratch, class_db(str(ctx.game)), map_sha256=manifest["source"]["original"]["sha256"],
                      classes_pack=ctx.hasher.sha256(pack) if pack.is_file() else None, render=render)
            a = {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}
            b = {p.relative_to(scratch).as_posix(): p.read_bytes() for p in scratch.rglob("*") if p.is_file()}
            if a.keys() != b.keys():
                raise ValueError(f"second build has other files: {sorted(set(a) ^ set(b))[:10]}")
            diff = sorted(k for k in a if a[k] != b[k])
            if diff:
                raise ValueError(f"second build differs in {len(diff)} file(s): {diff[:10]}")
        finally:
            shutil.rmtree(scratch, ignore_errors=True)
        return True
