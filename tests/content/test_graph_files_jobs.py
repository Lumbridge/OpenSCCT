"""Build graph: steps that own files instead of folders, and per-map steps in worker processes (--jobs)."""

from __future__ import annotations

from svmbuild.graph import Cache, Context, Graph, Step, StepOutput
from svmbuild.hashing import FileHasher


class Pack(Step):
    """Writes out/shared/pack.json (a file, next to other steps' files)."""

    name = "pack"
    version = 1

    def output_files(self, ctx, unit):
        return ["shared/pack.json"]

    def run(self, ctx, unit, out_dir):
        (ctx.out / "shared" / "pack.json").write_text("{}")
        return StepOutput()


class Neighbour(Step):
    """Owns out/shared/chars/ (a folder inside the folder that holds pack.json)."""

    name = "neighbour"
    version = 1

    def output_dir(self, ctx, unit):
        return ctx.out / "shared" / "chars"

    def run(self, ctx, unit, out_dir):
        (out_dir / "a.txt").write_text("a")
        return StepOutput()


class PerMap(Step):
    """Per map: writes the map name and the pack size; runs in workers when jobs > 1."""

    name = "permap"
    version = 1
    deps = ("pack",)
    per_map = True

    def output_dir(self, ctx, unit):
        return ctx.out / "maps" / unit

    def run(self, ctx, unit, out_dir):
        size = (ctx.out / "shared" / "pack.json").stat().st_size
        (out_dir / "out.txt").write_text(f"{unit}:{size}")
        if unit == "Bad":
            raise RuntimeError("bad map")
        return StepOutput(stats={"size": size})


def _ctx(tmp_path, jobs=1, maps=("A", "B", "C")):
    out = tmp_path / "out"
    return Context(game=tmp_path, out=out, hasher=FileHasher(out / "cache" / "filehash.json"),
                   cache=Cache(out / "cache"), maps=list(maps), log=lambda s: None, jobs=jobs)


def test_file_outputs_leave_neighbours_alone(tmp_path):
    g = Graph([Pack(), Neighbour()])
    r = {x.step: x for x in g.run(_ctx(tmp_path), ["pack", "neighbour"])}
    assert r["pack"].status == "built" and r["neighbour"].status == "built"
    out = tmp_path / "out"
    assert (out / "shared" / "pack.json").exists() and (out / "shared" / "chars" / "a.txt").exists()
    (out / "shared" / "pack.json").unlink()
    r = {x.step: x for x in g.run(_ctx(tmp_path), ["pack", "neighbour"])}
    assert r["pack"].status == "restored" and r["neighbour"].status == "cached"
    assert (out / "shared" / "chars" / "a.txt").exists()


def test_parallel_matches_sequential(tmp_path):
    g = Graph([Pack(), PerMap()])
    seq = g.run(_ctx(tmp_path / "s"), ["permap"])
    par = g.run(_ctx(tmp_path / "p", jobs=3), ["permap"])
    assert [(r.step, r.unit, r.status, r.key) for r in seq] == [(r.step, r.unit, r.status, r.key) for r in par]
    for m in ("A", "B", "C"):
        assert (tmp_path / "p" / "out" / "maps" / m / "out.txt").read_text() == f"{m}:2"
    again = g.run(_ctx(tmp_path / "p", jobs=3), ["permap"])
    assert [r.status for r in again] == ["cached", "cached", "cached", "cached"]


def test_parallel_failure_is_per_map(tmp_path):
    g = Graph([Pack(), PerMap()])
    res = g.run(_ctx(tmp_path, jobs=2, maps=("A", "Bad")), ["permap"])
    status = {(r.step, r.unit): r.status for r in res}
    assert status[("permap", "A")] == "built" and status[("permap", "Bad")] == "failed"
