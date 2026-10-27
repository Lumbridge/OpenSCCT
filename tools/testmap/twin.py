"""The .sdc twin of TestRange: the same layout built in the original's map editor, for recordings of the real game.

    python -I -B tools/testmap/twin.py build      the editor (sandbox, headless) builds the map from layout.py and saves
                                                   it: <sandbox>/Packages/MapsEd/TestRange.sdc and local/testrange/
    python -I -B tools/testmap/twin.py play       Play Level of the twin under the play lock: the spy spawns, is read
                                                   and the game is quit
    python -B tools/testmap/twin.py convert       the game form of the twin (what Play Level saved) through the content
                                                   pipeline's converter into local/testrange/content/svmap/TestRange
    python -B tools/testmap/twin.py diff          the converted twin against the SVMap generate.py writes

Everything the editor makes or the converter writes is game data: it stays under local/testrange/ (gitignored) and
in the sandbox. The editor is driven only through its automation channel, in the sandbox, on a hidden desktop, and
the game only through Play Level under gamewait.play_lock (re/tools/difftest/sandbox.py); every process this tool
starts is ended before it returns. docs/TESTMAP.md.
"""

from __future__ import annotations

import argparse
import json
import math
import os
import shutil
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(REPO / "re" / "tools" / "difftest"))

import layout as L  # noqa: E402

LOCAL = REPO / "local" / "testrange"
TWIN = LOCAL / L.MAP_ID / (L.MAP_ID + ".sdc")
# The game's form of the twin: Play Level saves the open map for the game as Packages/Maps/Autoplay.sdc (the editor's
# MapsEd form keeps editor data the converter does not read). Copied after a play; the converter reads this one.
TWIN_GAME = LOCAL / L.MAP_ID / (L.MAP_ID + ".game.sdc")
GAME_ROOT = LOCAL / "game"
CONTENT = LOCAL / "content"
NATIVE = REPO / ".content" / "svmap" / L.MAP_ID
REFERENCE = Path(os.path.expanduser(r"~\Desktop\Enhanced SCCT Versus v4.0 Public Beta 3"))
SAVE_ANSWER = [{"text": "larger than the file in memory", "press": "Yes"}]

# Change-file text for string properties (quoted); other strings are names or enum values and go as they are.
STRING_PROPS = {"TriggerName", "ObjectiveName", "Description", "DescriptionDEF", "CamName", "LocationName"}
GRATING_MESH = "SQU13_STM.Objects.SQU13_Grid01"
GAME_MODES = {5: "GM_Multi"}


def log(*parts):
    print("[twin %s] %s" % (time.strftime("%H:%M:%S"), " ".join(str(p) for p in parts)), file=sys.stderr, flush=True)


def tools_path(args) -> str:
    return args.re_tools or os.environ.get("SVM_RE_TOOLS") or os.path.expanduser(r"~\source\repos\SCCT_Versus_Reloaded_Editor_mb-main\tools\automation")


def _mapkit(args):
    path = tools_path(args)
    if path not in sys.path:
        sys.path.insert(0, path)
    import mapkit  # noqa: PLC0415

    return mapkit


def _prop(mk, key, value):
    if isinstance(value, bool):
        return "True" if value else "False"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        return mk.num(value)
    if isinstance(value, str):
        return mk.text(value) if key in STRING_PROPS else value
    if isinstance(value, dict) and "$actor" in value:
        return {"$ref": value["$actor"]}
    if isinstance(value, dict) and "$elements" in value:
        n = 1 + max(int(k) for k in value["$elements"])
        size = max(n, 5)
        return [_prop(mk, key, value["$elements"][str(i)]) if str(i) in value["$elements"] else "None" for i in range(size)]
    if isinstance(value, list):
        return [_prop(mk, key, v) for v in value]
    raise ValueError(f"{key}: no change-file form for {value!r}")


def changes(mk):
    """The change file of the twin: the carves, the solids, portals, zones, lights and the gameplay actors."""
    sys.path.insert(0, str(HERE))
    from generate import vitre_scale  # noqa: PLC0415

    c = mk.Changes("TestRange: the SVM test map, built from tools/testmap/layout.py.")
    mats = {k: m.stock for k, m in L.MATERIALS.items()}
    c.load(*sorted({p.split(".")[0] for p in mats.values()} | {"GAR_TXT", GRATING_MESH.split(".")[0]}))
    for cv in L.CARVES:
        c.carve(cv.name, list(cv.lo), list(cv.hi), {"floor": mats[cv.floor], "walls": mats[cv.walls], "ceiling": mats[cv.ceiling]})
    for s in L.SOLIDS:
        tex = {"all": mats[s.material]}
        if s.up:
            c.ramp(s.name, list(s.lo), list(s.hi), s.up, tex)
        else:
            c.solid(s.name, list(s.lo), list(s.hi), tex)
    # Zone portals in the gradient room's two doorways, in the middle of the wall.
    for side, x in (("W", 240.0), ("E", 2320.0)):
        c.portal(f"RoomPortal{side}", (x, L.DOOR_Y[0]), (x, L.DOOR_Y[1]), 0.0, L.DOOR_TOP)
    for li in L.LIGHTS:
        c.light(li.id, li.location, li.brightness, li.radius, li.hue, li.saturation, bAffectOwnZoneOnly="True",
                bApplyToInGameLighting="True")
    for a in L.ACTORS:
        props = {}
        for k, v in a.props.items():
            if a.cls == "SBase.SMission" and k == "GameMode":
                props[k] = GAME_MODES[v]
            else:
                props[k] = _prop(mk, k, v)
        if a.cls == "SBase.SVitre":
            props["DrawScale3D"] = mk.vec(vitre_scale())
        c.actor(a.id, a.cls, a.location, (a.pitch, a.yaw, 0), **props)
    gx = (L.PANEL_X[0] + L.PANEL_X[1]) / 2
    gy = sum(L.OPENINGS["grating"]) / 2
    c.mesh("Grating", GRATING_MESH, (gx, gy, (L.OPEN_Z[0] + L.OPEN_Z[1]) / 2), (0, 16384, 16384), bBulletGoTru="True")
    return c


def ge_commands(mk) -> list[tuple[str, str]]:
    return [(g.id, mk.ge_command(g.kind, [list(p) for p in g.points])) for g in L.GES]


def _call(live, cmd, args=None):
    reply = live.raw(cmd, args)
    if not reply.get("ok"):
        raise SystemExit(f"{cmd} failed: {json.dumps(reply.get('error'))[:1500]}")
    return reply.get("result")


def _bounds(a):
    b = a["bounds"]
    if isinstance(b, dict):
        lo = b.get("min") or b.get("Min") or b.get("lo")
        hi = b.get("max") or b.get("Max") or b.get("hi")
        if lo is None or hi is None:
            raise SystemExit(f"bounds in an unknown form: {b}")
        return lo, hi
    return b[0], b[1]


def _fit_grating(live, report):
    """Scales and moves the grating mesh so its bounds fill the grating opening (4 thick, as the SVMap's)."""
    gx = (L.PANEL_X[0] + L.PANEL_X[1]) / 2
    y0, y1 = L.OPENINGS["grating"]
    z0, z1 = L.OPEN_Z
    want = ((gx - 2.0, y0, z0), (gx + 2.0, y1, z1))
    found = _call(live, "actors.find", {"queries": [{"name": "Grating", "fields": ["name", "location", "scale", "bounds"]}]})
    actors = (found.get("results") or [found])[0].get("actors") or []
    if not actors:
        raise SystemExit("the grating mesh was not placed")
    a = actors[0]
    lo, hi = _bounds(a)
    report["gratingPlaced"] = a
    size = [hi[i] - lo[i] for i in range(3)]
    target = [want[1][i] - want[0][i] for i in range(3)]
    scale = a.get("scale") or [1.0, 1.0, 1.0]
    # The mesh is turned yaw 90, roll 90: its X runs along the world Y, its Y along Z and its Z along X.
    new_scale = [scale[0] * target[1] / size[1], scale[1] * target[2] / size[2], scale[2] * target[0] / size[0]]
    _call(live, "actors.set", {"changes": [{"name": "Grating", "props": {"DrawScale3D": {"X": new_scale[0], "Y": new_scale[1], "Z": new_scale[2]}}}]})
    found = _call(live, "actors.find", {"queries": [{"name": "Grating", "fields": ["name", "location", "bounds"]}]})
    a = (found.get("results") or [found])[0]["actors"][0]
    lo, hi = _bounds(a)
    centre = [(lo[i] + hi[i]) / 2 for i in range(3)]
    goal = [(want[0][i] + want[1][i]) / 2 for i in range(3)]
    delta = [goal[i] - centre[i] for i in range(3)]
    _call(live, "actors.move", {"moves": [{"name": "Grating", "delta": delta}]})
    found = _call(live, "actors.find", {"queries": [{"name": "Grating", "fields": ["name", "location", "scale", "bounds"]}]})
    report["grating"] = (found.get("results") or [found])[0]["actors"][0]


def build(args):
    import sandbox as sb  # noqa: PLC0415  re/tools/difftest/sandbox.py

    mk = _mapkit(args)
    LOCAL.mkdir(parents=True, exist_ok=True)
    TWIN.parent.mkdir(parents=True, exist_ok=True)
    work = LOCAL / "work"
    work.mkdir(exist_ok=True)
    c = changes(mk)
    problems = c.check()
    doc = c.write(str(work / "testrange-changes.json"))
    ges = ge_commands(mk)
    (work / "testrange-ges.txt").write_text("\n".join(cmd for _, cmd in ges) + "\n", encoding="utf-8")
    report = {"changesCheck": problems, "operations": len(c.document()["operations"]), "ges": len(ges)}
    log("change file", doc, "operations", report["operations"], "check", problems or "nothing")
    tools = sb.load_tools(tools_path(args))
    target = Path(args.sandbox) / "Packages" / "MapsEd" / (L.MAP_ID + ".sdc")
    with sb.Sandbox(tools, args.sandbox, fps=0) as box:
        live = box.live
        _call(live, "exec", {"command": "MAP NEW"})
        t = time.time()
        report["apply"] = _call(live, "map.apply", {"path": doc, "timeout": 1800})
        log("applied in %.0f s" % (time.time() - t))
        _fit_grating(live, report)
        found = _call(live, "actors.find", {"queries": [{"class": "LevelInfo", "exact": True, "fields": ["name"]}]})
        level = (found.get("results") or [found])[0]["actors"][0]["name"]
        _call(live, "actors.set", {"changes": [{"name": level, "props": {"Title": L.TITLE, "Author": "SVM"}}], "force": True})
        report["levelInfo"] = level
        handled = []
        for name, cmd in ges:
            handled.append((name, (_call(live, "exec", {"command": cmd}) or {}).get("handled")))
        report["gesHandled"] = handled
        t = time.time()
        built = _call(live, "build", {"kind": "all", "timeout": 1200})
        report["mapCheck"] = built.get("mapCheck") if isinstance(built, dict) else built
        log("built in %.0f s" % (time.time() - t))
        _call(live, "map.save", {"path": str(target), "answers": SAVE_ANSWER})
        t3d = LOCAL / "work" / "testrange.t3d"
        _call(live, "exec", {"command": 'MAP EXPORT FILE="%s"' % t3d})
        report["stats"] = _call(live, "level.stats", {})
        report["probes"] = probes(live)
    shutil.copy2(target, TWIN)
    report["saved"] = {"sandbox": str(target), "local": str(TWIN), "sha256": sb.sha256(str(TWIN))}
    report["processes"] = box.report.get("killed")
    (LOCAL / "build-report.json").write_text(json.dumps(report, indent=1, default=str), encoding="utf-8")
    bad = [p for p in report["probes"] if not p.get("ok")]
    log("saved", TWIN, "probes failing", len(bad))
    for p in bad:
        log("probe", json.dumps(p))
    return 1 if bad else 0


def probes(live) -> list[dict]:
    """The editor's own answers at the stations: the floor under every spot and the zone of the gradient room."""
    out = []
    spots = [(s.id, list(s.spot)) for s in L.STATIONS]
    pts = [[p[0], p[1], p[2] + 60.0] for _, p in spots]
    info = _call(live, "point.info", {"points": pts, "floor": True})
    results = info.get("results") if isinstance(info, dict) else info
    for (sid, spot), r in zip(spots, results or []):
        floor = (r.get("floor") or {}).get("z")
        out.append({"station": sid, "spot": spot, "floor": floor, "zone": r.get("zoneName"),
                    "ok": floor is not None and abs(floor - spot[2]) < 1.0})
    return out


def play(args):
    import sandbox as sb  # noqa: PLC0415

    tools = sb.load_tools(tools_path(args))
    report = {}
    with sb.Sandbox(tools, args.sandbox, fps=60) as box:
        box.load_map(str(Path(args.sandbox) / "Packages" / "MapsEd" / (L.MAP_ID + ".sdc")))
        with box.play(args.side, lock_timeout=args.lock_timeout) as (session, info):
            report["game"] = info
            state = session.wait(timeout=30)
            report["pawn"] = state.get("pawn")
            # Copied while this game holds the play lock: the sandbox is shared, and the next Play Level rewrites it.
            autoplay = Path(args.sandbox) / "Packages" / "Maps" / "Autoplay.sdc"
            if info.get("autoplaySha256") and sb.sha256(str(autoplay)) == info["autoplaySha256"]:
                shutil.copy2(autoplay, TWIN_GAME)
                report["gameForm"] = {"path": str(TWIN_GAME), "sha256": info["autoplaySha256"], "from": sb.sha256(str(TWIN))}
    report["processes"] = box.report.get("killed")
    if "gameForm" not in report:
        log("the game form of the twin was not captured (Autoplay.sdc changed under this game)")
    (LOCAL / f"play-{args.side}.json").write_text(json.dumps(report, indent=1, default=str), encoding="utf-8")
    log("pawn", json.dumps(report.get("pawn"), default=str)[:400])
    return 0


def _junction(link: Path, target: Path):
    if link.exists():
        return
    subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(target)], check=True, capture_output=True)


def convert(args):
    """A game root under local/ that is the reference install with the twin as its only map, then svmbuild maps."""
    if not TWIN_GAME.is_file():
        raise SystemExit(f"no game form of the twin at {TWIN_GAME}: run build, then play")
    (GAME_ROOT / "Packages" / "Maps").mkdir(parents=True, exist_ok=True)
    _junction(GAME_ROOT / "System", REFERENCE / "System")
    for sub in sorted((REFERENCE / "Packages").iterdir()):
        if sub.is_dir() and sub.name.lower() not in ("maps", "mapsed"):
            _junction(GAME_ROOT / "Packages" / sub.name, sub)
    shutil.copy2(TWIN_GAME, GAME_ROOT / "Packages" / "Maps" / TWIN.name)
    # The pipeline's converter on that root (the step graph's cache wants the game's files under one real folder).
    sys.path.insert(0, str(REPO / "content"))
    from svmbuild.hashing import sha256_file  # noqa: PLC0415
    from svmbuild.mapconv.build import build_map  # noqa: PLC0415
    from svmbuild.sources.classdb import class_db  # noqa: PLC0415
    from svmbuild.validation import validate_svmap  # noqa: PLC0415

    out = CONTENT / "svmap" / L.MAP_ID
    out.parent.mkdir(parents=True, exist_ok=True)
    t = time.time()
    stats = build_map(GAME_ROOT, L.MAP_ID, out, class_db(str(GAME_ROOT)), map_sha256=sha256_file(TWIN_GAME), render=True)
    errors = validate_svmap(out)
    stats["validation"] = errors
    (LOCAL / "convert-report.json").write_text(json.dumps(stats, indent=1, default=str), encoding="utf-8")
    log("converted in %.0f s: actors %s, colliders %s, ges %s, validation %s" % (
        time.time() - t, stats["actors"].get("actors"), stats["collision"]["colliders"], stats["traversal"].get("ges"),
        errors or "ok"))
    return 1 if errors else 0


def diff(args):
    sys.path.insert(0, str(HERE))
    import twindiff  # noqa: PLC0415

    report = twindiff.compare(NATIVE, CONTENT / "svmap" / L.MAP_ID)
    (LOCAL / "diff-report.json").write_text(json.dumps(report, indent=1, default=str), encoding="utf-8")
    for line in report["summary"]:
        print(line)
    return 0 if report["ok"] else 1


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python -I -B tools/testmap/twin.py", description=__doc__.split("\n\n")[0])
    ap.add_argument("command", choices=["build", "play", "convert", "diff"])
    ap.add_argument("--sandbox", default=r"F:\SCCT-Automation\svm\Sandbox")
    ap.add_argument("--re-tools", default=None)
    ap.add_argument("--side", default="spy", choices=["spy", "merc"])
    ap.add_argument("--lock-timeout", type=int, default=3600)
    args = ap.parse_args(argv)
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    return {"build": build, "play": play, "convert": convert, "diff": diff}[args.command](args)


if __name__ == "__main__":
    raise SystemExit(main())
