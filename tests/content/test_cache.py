"""Cache keying: a step reruns exactly when its inputs, version, options, code or upstream outputs change."""

from __future__ import annotations

import os
from pathlib import Path

import pytest
from svmbuild.graph import Cache, Context, Graph, Step, StepOutput
from svmbuild.hashing import FileHasher


class Source(Step):
    """Copies <game>/src.txt (plus a suffix option) into out/source/."""

    name = "source"
    version = 1
    suffix = ""
    runs = 0

    def options(self, ctx, unit):
        return {"suffix": self.suffix}

    def inputs(self, ctx, unit):
        return {"game:src.txt": ctx.digest("game:src.txt")}

    def output_dir(self, ctx, unit):
        return ctx.out / "source"

    def run(self, ctx, unit, out_dir):
        type(self).runs += 1
        text = (ctx.game / "src.txt").read_text() + self.suffix
        (out_dir / "data.txt").write_text(text)
        return StepOutput(stats={"chars": len(text)})


class Upper(Step):
    """Per map: upper-cases the source output, and discovers <game>/<map>.extra when present."""

    name = "upper"
    version = 1
    deps = ("source",)
    per_map = True
    runs = 0

    def output_dir(self, ctx, unit):
        return ctx.out / "upper" / unit

    def run(self, ctx, unit, out_dir):
        type(self).runs += 1
        text = (ctx.out / "source" / "data.txt").read_text().upper()
        extra = ctx.game / f"{unit}.extra"
        found = {f"game:{unit}.extra": ctx.digest(f"game:{unit}.extra")}
        if extra.exists():
            text += extra.read_text()
        (out_dir / "result.txt").write_text(text)
        return StepOutput(discovered={"files": found})


@pytest.fixture
def env(tmp_path):
    game = tmp_path / "game"
    game.mkdir()
    (game / "src.txt").write_text("hello")
    out = tmp_path / "out"
    Source.runs = Upper.runs = 0
    Source.suffix = ""
    Source.version = 1

    def run(**kw):
        hasher = FileHasher(out / "cache" / "filehash.json")
        ctx = Context(game=game, out=out, hasher=hasher, cache=Cache(out / "cache"), maps=["MapA", "MapB"],
                      log=lambda s: None, **kw)
        return {(r.step, r.unit): r for r in Graph([Source(), Upper()]).run(ctx, ["upper"])}

    return game, out, run


def _touch_later(p: Path) -> None:
    st = p.stat()
    os.utime(p, ns=(st.st_atime_ns, st.st_mtime_ns + 2_000_000_000))


def test_first_run_builds_then_caches(env):
    game, out, run = env
    r1 = run()
    assert {k: r.status for k, r in r1.items()} == {("source", None): "built", ("upper", "MapA"): "built",
                                                    ("upper", "MapB"): "built"}
    r2 = run()
    assert all(r.status == "cached" for r in r2.values())
    assert {k: r.key for k, r in r1.items()} == {k: r.key for k, r in r2.items()}
    assert (Source.runs, Upper.runs) == (1, 2)
    assert (out / "upper" / "MapA" / "result.txt").read_text() == "HELLO"


def test_input_change_propagates(env):
    game, out, run = env
    r1 = run()
    (game / "src.txt").write_text("world")
    _touch_later(game / "src.txt")
    r2 = run()
    assert r2[("source", None)].status == "built" and r2[("source", None)].key != r1[("source", None)].key
    assert r2[("upper", "MapA")].status == "built" and r2[("upper", "MapA")].key != r1[("upper", "MapA")].key
    assert (out / "upper" / "MapB" / "result.txt").read_text() == "WORLD"
    # Back to the old content: everything comes from the cache again.
    (game / "src.txt").write_text("hello")
    _touch_later(game / "src.txt")
    r3 = run()
    assert all(r.status in ("cached", "restored") for r in r3.values())
    assert r3[("upper", "MapA")].key == r1[("upper", "MapA")].key
    assert (out / "upper" / "MapA" / "result.txt").read_text() == "HELLO"


def test_same_upstream_output_keeps_downstream_key(env):
    game, out, run = env
    r1 = run()
    Source.version = 2          # new key for source, but identical output bytes
    r2 = run()
    assert r2[("source", None)].status == "built" and r2[("source", None)].key != r1[("source", None)].key
    assert r2[("upper", "MapA")].status == "cached" and r2[("upper", "MapA")].key == r1[("upper", "MapA")].key


def test_option_change_rekeys(env):
    game, out, run = env
    r1 = run()
    Source.suffix = "!"
    r2 = run()
    assert r2[("source", None)].key != r1[("source", None)].key
    assert (out / "upper" / "MapA" / "result.txt").read_text() == "HELLO!"


def test_discovered_input_invalidates(env):
    game, out, run = env
    r1 = run()
    (game / "MapB.extra").write_text("+b")
    r2 = run()
    assert r2[("upper", "MapA")].status == "cached"
    assert r2[("upper", "MapB")].status == "built"
    assert r2[("upper", "MapB")].key == r1[("upper", "MapB")].key   # same key, entry rejected by its discovery
    assert (out / "upper" / "MapB" / "result.txt").read_text() == "HELLO+b"


def test_restore_after_output_damage(env):
    game, out, run = env
    run()
    (out / "upper" / "MapA" / "result.txt").write_text("tampered")
    (out / "upper" / "MapA" / "stray.txt").write_text("x")
    (out / "source" / "data.txt").unlink()
    r = run()
    assert r[("source", None)].status == "restored"
    assert r[("upper", "MapA")].status == "restored"
    assert (out / "upper" / "MapA" / "result.txt").read_text() == "HELLO"
    assert not (out / "upper" / "MapA" / "stray.txt").exists()
    assert (Source.runs, Upper.runs) == (1, 2)


def test_force_rebuilds(env):
    game, out, run = env
    run()
    r = run(force=True)
    assert all(x.status == "built" for x in r.values())


def test_key_is_canonical_json_sha256():
    m = {"step": "x", "version": 1, "options": {"b": 1.0, "a": [1, 2]}}
    import hashlib

    assert Graph.key_of(m) == hashlib.sha256(b'{"options":{"a":[1,2],"b":1.0},"step":"x","version":1}').hexdigest()


def test_failure_blocks_dependents(tmp_path):
    class Boom(Source):
        name = "source"

        def run(self, ctx, unit, out_dir):
            raise RuntimeError("boom")

    game = tmp_path / "game"
    game.mkdir()
    (game / "src.txt").write_text("x")
    ctx = Context(game=game, out=tmp_path / "out", hasher=FileHasher(None), cache=None, maps=["M"],
                  log=lambda s: None)
    res = Graph([Boom(), Upper()]).run(ctx, ["upper"])
    assert [(r.step, r.status) for r in res] == [("source", "failed"), ("upper", "blocked")]


def test_cycle_and_unknown_dependency_rejected():
    class A(Step):
        name = "a"
        deps = ("b",)

    class B(Step):
        name = "b"
        deps = ("a",)

    with pytest.raises(ValueError):
        Graph([A(), B()])

    class Lonely(Step):
        name = "lonely"
        deps = ("missing",)

    with pytest.raises(ValueError):
        Graph([Lonely()])


def test_output_outside_out_is_refused(tmp_path):
    class Escape(Step):
        name = "escape"

        def output_dir(self, ctx, unit):
            return tmp_path / "elsewhere"

        def run(self, ctx, unit, out_dir):  # pragma: no cover - never reached
            raise AssertionError

    ctx = Context(game=tmp_path, out=tmp_path / "out", hasher=FileHasher(None), cache=None, log=lambda s: None)
    (res,) = Graph([Escape()]).run(ctx, ["escape"])
    assert res.status == "failed" and "outside" in res.error
