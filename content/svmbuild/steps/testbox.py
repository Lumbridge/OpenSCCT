"""testbox: the synthetic TestBox SVMap built into maps/TestBox/ and validated.

The tracked copy in fixtures/testbox/ is written by `python -m svmbuild.testbox`; this step builds the same bytes
into the content folder so the rest of the pipeline can use it like any converted map.
"""

from __future__ import annotations

from pathlib import Path

from ..graph import Context, Step, StepOutput


class TestBoxStep(Step):
    __test__ = False  # not a pytest class
    name = "testbox"
    version = 1
    description = "Synthetic TestBox SVMap, no game data (maps/TestBox/)."
    code_files = ("content/svmbuild/testbox.py", "content/svmbuild/canonical.py", "content/svmbuild/svmap.py",
                  "content/svmbuild/writers/collision.py", "content/svmbuild/writers/glb.py")

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return ctx.out / "maps" / "TestBox"

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        assert out_dir is not None
        from ..testbox import generate  # noqa: PLC0415
        from ..validation import validate_svmap  # noqa: PLC0415

        manifest = generate(out_dir)
        errors = validate_svmap(out_dir)
        if errors:
            raise ValueError("TestBox failed validation:\n" + "\n".join(errors))
        return StepOutput(stats={"sim": manifest["hashes"]["sim"][:16], "files": len(manifest["files"])})
