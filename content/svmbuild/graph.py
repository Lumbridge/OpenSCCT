"""Step graph and content-addressed build cache.

A step runs once per map (per_map) or once per build. Its cache key is the sha256 of the canonical JSON of its
"material": step name, step version, a digest of its code, its options, the digests of its declared inputs and the
output digests of the steps it depends on. A step may also report inputs it discovered while running (the packages a
map export actually opened); those are stored in the cache entry and checked again before the entry is reused.

A step owns either a whole folder (`output_dir`, cleared before it runs) or a list of files (`output_files`, paths
under the content root; only those files are replaced), so several steps can write next to each other in one folder.

Cache layout under <out>/cache:
    entries/<step>/<key>.json     material, discovered inputs, output listing, stats of one build
    objects/<aa>/<sha256>         output file contents, shared by every entry
    filehash.json                 (size, mtime) -> sha256 memo for input and output files

A hit restores the step's outputs from the object store if they are missing or changed; a miss clears them, runs the
step and stores the result.

With jobs > 1, consecutive per-map steps run map by map in worker processes: each worker runs the whole chain of
those steps for one map, with its own file hasher whose new digests are merged back into the parent's memo. A global
step marked `background` (long, and nothing in the build reads its files) then runs in a process of its own beside
the rest of the plan; a step that depends on it waits for it, and the run waits for it at the end.
"""

from __future__ import annotations

import hashlib
import inspect
import json
import os
import shutil
import time
import traceback
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Iterable, Sequence

from . import canonical
from .hashing import FileHasher
from .paths import REPO_ROOT, is_within

MATERIAL_FORMAT = 1


@dataclass
class StepOutput:
    discovered: dict = field(default_factory=dict)
    stats: dict = field(default_factory=dict)
    # A step may decline to cache a result (for example "nothing to do yet").
    cacheable: bool = True
    status: str = "built"


@dataclass
class UnitResult:
    step: str
    unit: str | None
    status: str          # built | cached | restored | stub | skipped | failed | blocked
    key: str
    seconds: float = 0.0
    stats: dict = field(default_factory=dict)
    error: str | None = None

    @property
    def ok(self) -> bool:
        return self.status not in ("failed", "blocked")


@dataclass
class Context:
    game: Path
    out: Path
    hasher: FileHasher
    cache: "Cache | None"
    maps: list[str] = field(default_factory=list)
    force: bool = False
    log: Callable[[str], None] = print
    # Build-wide switches that are not step options (for example validate's --check-stable).
    options: dict = field(default_factory=dict)
    jobs: int = 1

    def resolve(self, logical: str) -> Path:
        scheme, sep, rel = logical.partition(":")
        if not sep:
            raise ValueError(f"logical path without scheme: {logical!r}")
        if scheme == "game":
            return self.game / rel
        if scheme == "addons":
            from .gamefiles import addons_root  # noqa: PLC0415

            root = addons_root()
            return (root if root is not None else self.game / ".no-addons") / rel
        if scheme == "repo":
            return REPO_ROOT / rel
        if scheme == "out":
            return self.out / rel
        raise ValueError(f"unknown scheme in {logical!r}")

    def game_path(self, path: Path) -> str:
        """game:<rel> for a file of the install, addons:<rel> for one of the downloaded maps folder."""
        from .gamefiles import addons_root  # noqa: PLC0415

        resolved = Path(path).resolve()
        root = addons_root()
        if root is not None and is_within(resolved, root) and not is_within(resolved, self.game):
            return "addons:" + resolved.relative_to(root.resolve()).as_posix()
        return "game:" + resolved.relative_to(self.game.resolve()).as_posix()

    def digest(self, logical: str) -> str | None:
        return self.hasher.maybe(self.resolve(logical))


class Step:
    name: str = ""
    version: int = 1
    deps: tuple[str, ...] = ()
    # Steps whose files live inside this step's folder: a rebuild or restore of this step removes them, so with
    # dependencies on, the plan runs them after it (the maps step and audio-map).
    followers: tuple[str, ...] = ()
    per_map: bool = False
    # With jobs > 1, run beside the rest of the plan in a process of its own (global steps only). Only for a step
    # whose files no other step reads unless it lists this step in its deps.
    background: bool = False
    stub: bool = False
    needs_game: bool = False
    description: str = ""
    # Extra source files (repo-relative) whose content is part of the key, beyond the step's own module.
    code_files: tuple[str, ...] = ()

    def options(self, ctx: Context, unit: str | None) -> dict:
        return {}

    def inputs(self, ctx: Context, unit: str | None) -> dict[str, str | None]:
        """Logical input name -> sha256 (None: must not exist)."""
        return {}

    def output_dir(self, ctx: Context, unit: str | None) -> Path | None:
        return None

    def output_files(self, ctx: Context, unit: str | None) -> list[str] | None:
        """Paths relative to ctx.out that this step writes, instead of owning a whole folder (None: folder mode)."""
        return None

    def run(self, ctx: Context, unit: str | None, out_dir: Path | None) -> StepOutput:
        raise NotImplementedError(f"{self.name} is not implemented yet")

    def discovered_ok(self, ctx: Context, unit: str | None, discovered: dict) -> bool:
        for logical, digest in discovered.get("files", {}).items():
            if ctx.digest(logical) != digest:
                return False
        return True

    def extra_code(self) -> list[Path]:
        """Absolute paths of more files whose content is part of the key (for example vendored readers)."""
        return []

    def code_digest(self, hasher: FileHasher) -> str:
        files = {Path(inspect.getsourcefile(type(self)) or "").resolve()}
        files.update((REPO_ROOT / f).resolve() for f in self.code_files)
        files.update(Path(p).resolve() for p in self.extra_code())
        return hasher.tree(REPO_ROOT, sorted(files))


@dataclass
class Outputs:
    """Where a unit writes: a folder it owns (files None) or the listed files under base."""
    base: Path
    files: list[str] | None = None

    def listing(self) -> list[Path]:
        if self.files is None:
            return _files_under(self.base) if self.base.exists() else []
        return [self.base / f for f in sorted(self.files) if (self.base / f).is_file()]

    def rel(self, p: Path) -> str:
        return p.relative_to(self.base).as_posix()

    def clear(self) -> None:
        if self.files is None:
            if self.base.exists():
                shutil.rmtree(self.base)
            self.base.mkdir(parents=True)
        else:
            for f in self.files:
                p = self.base / f
                if p.is_file():
                    p.unlink()
                p.parent.mkdir(parents=True, exist_ok=True)


def step_outputs(ctx: Context, step: Step, unit: str | None) -> Outputs | None:
    files = step.output_files(ctx, unit)
    if files is not None:
        return Outputs(ctx.out, list(files))
    d = step.output_dir(ctx, unit)
    return Outputs(d) if d is not None else None


def outputs_digest(outputs: dict[str, dict]) -> str:
    listing = "".join(f"{v['sha256']}  {k}\n" for k, v in sorted(outputs.items()))
    return hashlib.sha256(listing.encode("utf-8")).hexdigest()


def _files_under(root: Path) -> list[Path]:
    return sorted((p for p in root.rglob("*") if p.is_file()), key=lambda p: p.relative_to(root).as_posix())


def _atomic_copy(src: Path, dest: Path) -> None:
    """Copy into a content-addressed path. Another worker may write the same object at the same moment; on Windows
    the second rename then fails, and the object it would have written is already there."""
    dest.parent.mkdir(parents=True, exist_ok=True)
    tmp = dest.with_name(f"{dest.name}.{os.getpid()}.tmp")
    shutil.copyfile(src, tmp)
    for attempt in range(20):
        try:
            os.replace(tmp, dest)
            return
        except PermissionError:
            if dest.exists() and dest.stat().st_size == tmp.stat().st_size:
                tmp.unlink(missing_ok=True)
                return
            time.sleep(0.05 * (attempt + 1))
    os.replace(tmp, dest)


class Cache:
    def __init__(self, root: Path):
        self.root = Path(root)
        self.entries = self.root / "entries"
        self.objects = self.root / "objects"

    def _entry_path(self, step: str, key: str) -> Path:
        return self.entries / step / f"{key}.json"

    def _object_path(self, sha: str) -> Path:
        return self.objects / sha[:2] / sha

    def load(self, step: str, key: str) -> dict | None:
        p = self._entry_path(step, key)
        if not p.exists():
            return None
        try:
            return json.loads(p.read_text(encoding="utf-8"))
        except (OSError, ValueError):
            return None

    def store(self, step: str, key: str, material: dict, out: Outputs | None, output: StepOutput,
              hasher: FileHasher, seconds: float) -> dict:
        outputs: dict[str, dict] = {}
        if out is not None:
            for p in out.listing():
                sha = hasher.sha256(p)
                outputs[out.rel(p)] = {"sha256": sha, "size": p.stat().st_size}
                obj = self._object_path(sha)
                if not obj.exists():
                    _atomic_copy(p, obj)
        entry = {
            "step": step,
            "key": key,
            "material": material,
            "discovered": output.discovered,
            "outputs": outputs,
            "outputsDigest": outputs_digest(outputs) if out is not None else key,
            "stats": output.stats,
            "seconds": round(seconds, 3),
        }
        p = self._entry_path(step, key)
        p.parent.mkdir(parents=True, exist_ok=True)
        tmp = p.with_name(f"{p.name}.{os.getpid()}.tmp")
        tmp.write_text(json.dumps(entry, indent=1, sort_keys=True), encoding="utf-8")
        os.replace(tmp, p)
        return entry

    def restore(self, entry: dict, out: Outputs | None, hasher: FileHasher) -> str | None:
        """'cached' when the outputs already match, 'restored' after copying, None when an object is missing."""
        if out is None:
            return "cached"
        outputs: dict[str, dict] = entry.get("outputs", {})
        todo = []
        for rel, meta in outputs.items():
            dest = out.base / rel
            if dest.is_file() and dest.stat().st_size == meta["size"] and hasher.sha256(dest) == meta["sha256"]:
                continue
            obj = self._object_path(meta["sha256"])
            if not obj.exists():
                return None
            todo.append((dest, obj))
        extra = [p for p in out.listing() if out.rel(p) not in outputs]
        if not todo and not extra:
            return "cached"
        for p in extra:
            p.unlink()
        for dest, obj in todo:
            dest.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(obj, dest)
        if out.files is None:
            _prune_empty_dirs(out.base)
        return "restored"


def _prune_empty_dirs(root: Path) -> None:
    for d in sorted((p for p in root.rglob("*") if p.is_dir()), key=lambda p: len(p.parts), reverse=True):
        try:
            d.rmdir()
        except OSError:
            pass


class Graph:
    def __init__(self, steps: Iterable[Step]):
        self.steps: dict[str, Step] = {}
        for s in steps:
            if s.name in self.steps:
                raise ValueError(f"duplicate step {s.name}")
            self.steps[s.name] = s
        for s in self.steps.values():
            for d in s.deps + s.followers:
                if d not in self.steps:
                    raise ValueError(f"step {s.name} depends on unknown step {d}")
        self.order(list(self.steps))  # rejects cycles

    def order(self, targets: Sequence[str], with_deps: bool = True) -> list[Step]:
        for t in targets:
            if t not in self.steps:
                raise KeyError(t)
        wanted: set[str] = set()

        def visit(n: str, stack: tuple[str, ...]) -> None:
            if n in stack:
                raise ValueError("step cycle: " + " -> ".join(stack + (n,)))
            if n in wanted:
                return
            if with_deps:
                for d in self.steps[n].deps:
                    visit(d, stack + (n,))
            wanted.add(n)
            if with_deps:
                for f in self.steps[n].followers:
                    visit(f, ())

        for t in targets:
            visit(t, ())
        # Kahn's algorithm, ties broken by registration order, so the order is stable.
        names = [n for n in self.steps if n in wanted]
        done: list[str] = []
        pending = list(names)
        while pending:
            for n in pending:
                if all(d in done or d not in wanted for d in self.steps[n].deps):
                    done.append(n)
                    pending.remove(n)
                    break
            else:  # pragma: no cover - visit() already rejects cycles
                raise ValueError("step cycle among " + ", ".join(pending))
        return [self.steps[n] for n in done]

    def material(self, step: Step, ctx: Context, unit: str | None, dep_digests: dict[str, str]) -> dict:
        return {
            "format": MATERIAL_FORMAT,
            "step": step.name,
            "version": step.version,
            "unit": unit or "",
            "code": step.code_digest(ctx.hasher),
            "options": step.options(ctx, unit),
            "inputs": step.inputs(ctx, unit) if not step.stub else {},
            "deps": dep_digests,
        }

    @staticmethod
    def key_of(material: dict) -> str:
        return hashlib.sha256(canonical.dumps(material).encode("utf-8")).hexdigest()

    def _dep_digest(self, ctx: Context, dep: Step, unit: str | None, produced: dict) -> str:
        if (dep.name, unit) in produced:
            return produced[(dep.name, unit)]
        # Not part of this run (--no-deps): use whatever its outputs hold now.
        out = step_outputs(ctx, dep, unit)
        if out is None:
            return "absent"
        files = out.listing()
        if not files:
            return "absent"
        return outputs_digest({out.rel(p): {"sha256": ctx.hasher.sha256(p)} for p in files})

    def _dep_units(self, ctx: Context, step: Step, unit: str | None) -> list[tuple[Step, str | None]]:
        dep_units: list[tuple[Step, str | None]] = []
        for d in step.deps:
            dstep = self.steps[d]
            if dstep.per_map and not step.per_map:
                dep_units.extend((dstep, u) for u in ctx.maps)
            else:
                dep_units.append((dstep, unit if dstep.per_map else None))
        return dep_units

    def run_one(self, ctx: Context, step: Step, unit: str | None, produced: dict, failed: set) -> UnitResult:
        dep_units = self._dep_units(ctx, step, unit)
        if any((ds.name, du) in failed for ds, du in dep_units):
            failed.add((step.name, unit))
            return UnitResult(step.name, unit, "blocked", "")
        deps = {f"{ds.name}/{du or ''}": self._dep_digest(ctx, ds, du, produced) for ds, du in dep_units}
        try:
            material = self.material(step, ctx, unit, deps)
        except Exception as e:  # noqa: BLE001 - reported per unit
            failed.add((step.name, unit))
            return UnitResult(step.name, unit, "failed", "", error=f"{type(e).__name__}: {e}")
        key = self.key_of(material)
        res = self._run_unit(ctx, step, unit, material, key, produced)
        if not res.ok:
            failed.add((step.name, unit))
        return res

    def run(self, ctx: Context, targets: Sequence[str], with_deps: bool = True) -> list[UnitResult]:
        plan = self.order(targets, with_deps)
        results: list[UnitResult] = []
        produced: dict[tuple[str, str | None], str] = {}
        failed: set[tuple[str, str | None]] = set()
        background: dict = {}   # step name -> future of its process
        pool = None
        try:
            i = 0
            while i < len(plan):
                step = plan[i]
                if step.per_map and ctx.jobs > 1 and len(ctx.maps) > 1:
                    j = i
                    while j < len(plan) and plan[j].per_map:
                        j += 1
                    self._await_background(ctx, background, {d for s in plan[i:j] for d in s.deps}, produced,
                                           failed, results)
                    results.extend(self._run_parallel(ctx, plan[i:j], produced, failed))
                    i = j
                    continue
                self._await_background(ctx, background, set(step.deps), produced, failed, results)
                if step.background and not step.per_map and ctx.jobs > 1:
                    if pool is None:
                        import concurrent.futures as cf  # noqa: PLC0415
                        import multiprocessing as mp  # noqa: PLC0415

                        pool = cf.ProcessPoolExecutor(max_workers=1, mp_context=mp.get_context("spawn"))
                    shared = {k: v for k, v in produced.items() if k[1] is None}
                    background[step.name] = pool.submit(_worker, _context_spec(ctx), list(self.steps.values()),
                                                        [step.name], None, shared, set(failed), ctx.jobs)
                    i += 1
                    continue
                units: list[str | None] = list(ctx.maps) if step.per_map else [None]
                for unit in units:
                    res = self.run_one(ctx, step, unit, produced, failed)
                    results.append(res)
                    ctx.log(_line(res))
                i += 1
            self._await_background(ctx, background, set(background), produced, failed, results)
        finally:
            if pool is not None:
                pool.shutdown(wait=True, cancel_futures=True)
        ctx.hasher.save()
        return results

    def _await_background(self, ctx: Context, background: dict, names: set, produced: dict, failed: set,
                          results: list) -> None:
        """Waits for the background steps among names and takes in their results."""
        for name in [n for n in background if n in names]:
            fut = background.pop(name)
            try:
                unit_results, unit_produced, unit_failed, hashes = fut.result()
            except Exception as e:  # noqa: BLE001 - a dead worker fails its step, not the build
                unit_results = [UnitResult(name, None, "failed", "", error=f"worker: {type(e).__name__}: {e}")]
                unit_produced, unit_failed, hashes = {}, {(name, None)}, {}
            produced.update(unit_produced)
            failed.update(unit_failed)
            ctx.hasher.merge(hashes)
            for r in unit_results:
                ctx.log(_line(r))
            results.extend(unit_results)

    def _run_parallel(self, ctx: Context, steps: list[Step], produced: dict, failed: set) -> list[UnitResult]:
        import concurrent.futures as cf
        import multiprocessing as mp

        names = [s.name for s in steps]
        spec = _context_spec(ctx)
        shared = {k: v for k, v in produced.items() if k[1] is None}
        blocked = {k for k in failed if k[1] is None}
        results: list[UnitResult] = []
        with cf.ProcessPoolExecutor(max_workers=ctx.jobs, mp_context=mp.get_context("spawn")) as pool:
            steps_all = list(self.steps.values())
            futures = {pool.submit(_worker, spec, steps_all, names, unit, shared, blocked): unit
                       for unit in ctx.maps}
            for fut in cf.as_completed(futures):
                unit = futures[fut]
                try:
                    unit_results, unit_produced, unit_failed, hashes = fut.result()
                except Exception as e:  # noqa: BLE001 - a dead worker fails its map, not the build
                    unit_results = [UnitResult(n, unit, "failed", "", error=f"worker: {type(e).__name__}: {e}")
                                    for n in names]
                    unit_produced, unit_failed, hashes = {}, {(n, unit) for n in names}, {}
                produced.update(unit_produced)
                failed.update(unit_failed)
                ctx.hasher.merge(hashes)
                for r in unit_results:
                    ctx.log(_line(r))
                results.extend(unit_results)
        order = {n: i for i, n in enumerate(names)}
        maps = {m: i for i, m in enumerate(ctx.maps)}
        results.sort(key=lambda r: (order[r.step], maps.get(r.unit, 0)))
        return results

    def _run_unit(self, ctx: Context, step: Step, unit: str | None, material: dict, key: str,
                  produced: dict) -> UnitResult:
        if step.stub:
            produced[(step.name, unit)] = key
            return UnitResult(step.name, unit, "stub", key)
        out = step_outputs(ctx, step, unit)
        if out is not None:
            inside = all(is_within(out.base / f, ctx.out) for f in out.files) if out.files is not None else \
                is_within(out.base, ctx.out) and out.base.resolve() != ctx.out.resolve()
            if not inside:
                return UnitResult(step.name, unit, "failed", key, error=f"output folder {out.base} is outside {ctx.out}")
        t0 = time.perf_counter()
        if ctx.cache is not None and not ctx.force:
            entry = ctx.cache.load(step.name, key)
            if entry is not None and step.discovered_ok(ctx, unit, entry.get("discovered", {})):
                state = ctx.cache.restore(entry, out, ctx.hasher)
                if state is not None:
                    produced[(step.name, unit)] = entry["outputsDigest"]
                    return UnitResult(step.name, unit, state, key, time.perf_counter() - t0, entry.get("stats", {}))
        if out is not None:
            out.clear()
        try:
            output = step.run(ctx, unit, out.base if out is not None else None)
        except Exception as e:  # noqa: BLE001 - reported per unit
            return UnitResult(step.name, unit, "failed", key, time.perf_counter() - t0,
                              error=f"{type(e).__name__}: {e}\n{traceback.format_exc()}")
        seconds = time.perf_counter() - t0
        if ctx.cache is not None and output.cacheable:
            entry = ctx.cache.store(step.name, key, material, out, output, ctx.hasher, seconds)
            produced[(step.name, unit)] = entry["outputsDigest"]
        else:
            produced[(step.name, unit)] = key
        return UnitResult(step.name, unit, output.status, key, seconds, output.stats)


def _context_spec(ctx: Context) -> dict:
    return {"game": str(ctx.game), "out": str(ctx.out), "maps": list(ctx.maps), "force": ctx.force,
            "cache": ctx.cache is not None, "options": dict(ctx.options),
            "hasher_store": str(ctx.hasher.store) if ctx.hasher.store is not None else None,
            "trust_stat": ctx.hasher.trust_stat}


def _worker(spec: dict, steps: list, names: list[str], unit: str | None, shared: dict, blocked: set, jobs: int = 1):
    """Runs the steps `names` for one map (or a background global step, unit None) in a worker process (the graph's
    own step objects, pickled). jobs is what the steps see as ctx.jobs."""

    out = Path(spec["out"])
    store = Path(spec["hasher_store"]) if spec["hasher_store"] else None
    hasher = FileHasher(store, trust_stat=spec["trust_stat"], read_only=True)
    lines: list[str] = []
    ctx = Context(game=Path(spec["game"]), out=out, hasher=hasher,
                  cache=Cache(out / "cache") if spec["cache"] else None, maps=list(spec["maps"]),
                  force=spec["force"], log=lines.append, options=spec["options"], jobs=jobs)
    graph = Graph(steps)
    produced = dict(shared)
    failed = set(blocked)
    results = []
    for n in names:
        results.append(graph.run_one(ctx, graph.steps[n], unit, produced, failed))
    mine = {k: v for k, v in produced.items() if k[1] == unit}
    return results, mine, {k for k in failed if k[1] == unit}, hasher.new_entries()


def _line(r: UnitResult) -> str:
    unit = r.unit or "-"
    extra = ""
    if r.stats:
        extra = "  " + " ".join(f"{k}={v}" for k, v in sorted(r.stats.items()) if not isinstance(v, (dict, list)))
    head = f"{r.step:13s} {unit:12s} {r.status:9s} {r.seconds:7.2f}s  {r.key[:12]}{extra}"
    if r.error:
        head += "\n    " + r.error.strip().replace("\n", "\n    ")
    return head
