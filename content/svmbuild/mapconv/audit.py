"""Read-only map parity inventory; source comparisons and reports stay local.

Run with PYTHONPATH=content: python -m svmbuild.mapconv.audit --source
This checks conversion coverage, not whether every authored interaction is playable.
"""

from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path

from .. import canonical
from ..paths import REPO_ROOT, default_game_root, default_out_root
from ..svmap import all_actors, actor_refs, derive_event_edges, derive_tag_index


FAMILIES = {
    "movers": ("Engine.Mover",),
    "lifts": ("SBase.SLift",),
    "lift_doors": ("SBase.SLiftDoor",),
    "rotating_movers": ("Engine.MoverRotating",),
    "switches": ("SBase.SInterrupteur",),
    "hackable_objectives": ("SBase.SComputerObjectiveTrigger",),
    "objectives": ("SBase.SObjectiveTrigger",),
    "items": ("SBase.SDisk", "SBase.SFlag", "SBase.SAmmoStock", "SBase.SInfirmary"),
    "gameplay_props": ("SBase.SGamePlayObject",),
    "damage_volumes": ("SBase.SDamageVolume",),
    "triggers": ("Engine.Trigger", "SBase.SPresenceTrigger"),
    "event_relays": ("SBase.SMagicEvent",),
    "animated_props": ("SBase.SAnimatedMesh",),
    "emitters": ("Engine.Emitter",),
    "security": ("SBase.SCamera", "SBase.SCamNetwork", "SBase.SVolumetricSensor", "SBase.SLaserTrigger"),
}


def ancestry(cls: str, classes: dict) -> set[str]:
    result: set[str] = set()
    while cls and cls not in result:
        result.add(cls)
        cls = classes.get(cls, {}).get("super")
    return result


def audit_document(gameplay: dict, classes: dict, fx: dict | None = None, lighting: dict | None = None) -> dict:
    """Validate actor/reference/event/animation coverage without mutating the document.

    Unmatched events are informational: source maps also contain unused events and
    engine-consumed names. Family counts overlap (lifts are movers, for example).
    """
    actors = all_actors(gameplay)
    by_id = {a["id"].casefold(): a for a in actors}
    animations = {key.casefold(): value for key, value in gameplay.get("animations", {}).items()}
    issues: list[dict] = []

    def issue(kind, actor, detail):
        issues.append({"kind": kind, "actor": actor, "detail": detail})

    duplicates = [key for key, count in Counter(a["id"].casefold() for a in actors).items() if count > 1]
    for key in duplicates:
        issue("duplicate_actor", key, "actor id is not unique")
    counts = Counter()
    for actor in actors:
        cls, props, key = actor["class"], actor["props"], actor["id"]
        chain = ancestry(cls, classes)
        effective = {**classes.get(cls, {}).get("defaults", {}), **props}
        for family, bases in FAMILIES.items():
            if chain.intersection(bases):
                counts[family] += 1
        if "SBase.SGamePlayObject" in chain and effective.get("bDamageable"):
            counts["destructibles"] += 1
        if cls not in classes:
            issue("missing_class", key, cls)
        for ref in actor_refs(props):
            if ref.casefold() not in by_id:
                issue("missing_actor_reference", key, ref)
        if fx is not None and "Engine.Emitter" in chain and key not in fx.get("emitters", {}):
            issue("missing_emitter", key, cls)
        if "SBase.SAnimatedMesh" in chain:
            mesh = effective.get("Mesh") or {}
            sequences = animations.get(mesh.get("$asset", "").casefold(), {}).get("sequences", {})
            sequence_names = {s.lower() for s in sequences}
            for seq in effective.get("Animations", []):
                if seq and seq.lower() != "none" and seq.lower() not in sequence_names:
                    issue("missing_animation", key, seq)

    edges = derive_event_edges(gameplay)
    if edges != gameplay.get("eventEdges"):
        issue("event_index_mismatch", "", "stored index differs from actor properties and animation notifies")
    if derive_tag_index(gameplay) != gameplay.get("tagIndex"):
        issue("tag_index_mismatch", "", "stored index differs from actor tags")
    unmatched = [e for e in edges if e.get("kind") != "accept" and not e["to"]]
    for light in (lighting or {}).get("lights", []):
        props = light.get("props", {})
        # Plain Light inherits Actor's DT_Sprite; bHidden defaults true. These are world-size sprites,
        # independent of LightType (LT_None may still draw) and separate from distance-scaled coronas.
        visible_sprite = (props.get("bHidden") is False and
                          props.get("DrawType", 1) == 1 and not light.get("corona") and
                          not props.get("bCorona", False) and props.get("Texture"))
        if visible_sprite:
            counts["light_sprites"] += 1
            if not light.get("sprite", {}).get("texture_uri"):
                issue("missing_light_sprite", light["name"], "visible light texture is not exported as a sprite")
    return {
        "map": gameplay["map"], "actors": len(actors), "families": dict(sorted(counts.items())),
        "render_fx": "checked" if fx is not None else "not present; not checked",
        "light_sprites": "checked" if lighting is not None else "not present; not checked",
        "events": len(edges), "notify_events": sum(e.get("kind") == "notify" for e in edges),
        "unmatched_events": unmatched, "issues": issues,
    }


def compare_source(src, gameplay: dict) -> dict:
    """Compare every source gameplay actor/property, preserving source exclusions.

    Values are compared in SVMap's canonical float32 form. No game text or raw
    property values are copied into the report.
    """
    from .gameplay import actor_json  # noqa: PLC0415

    expected = {a.name: a for a in src.of("level", "gameplay")}
    actual = {a["id"]: a for a in all_actors(gameplay)}
    missing = sorted(expected.keys() - actual.keys())
    extra = sorted(actual.keys() - expected.keys())
    differences = []
    for key in sorted(expected.keys() & actual.keys()):
        wanted = actor_json(src, expected[key])
        got = actual[key]
        if wanted["class"] != got["class"]:
            differences.append({"actor": key, "property": "class"})
        for prop in sorted(wanted["props"].keys() | got["props"].keys()):
            # Conversion writes this engine default explicitly even when omitted in the package.
            if expected[key].category == "level" and prop == "GEActivationMask":
                wanted["props"][prop] = int(src.value(expected[key], prop, 0) or 0)
            if (prop not in wanted["props"] or prop not in got["props"] or
                    canonical.dumps(wanted["props"][prop]) != canonical.dumps(got["props"][prop])):
                differences.append({"actor": key, "property": prop})
    errors = [{"actor": a.name, "error": a.error} for a in src.actors if a.error]
    # A discarded actor explicitly addressed by a gameplay event merits review.
    omitted_tags = {}
    from .gameplay import effective_tag  # noqa: PLC0415

    for actor in src.actors:
        if actor.category == "render":
            omitted_tags.setdefault(effective_tag(src, actor).lower(), []).append(actor.name)
    omitted_targets = []
    for edge in derive_event_edges(gameplay):
        if edge.get("kind") != "accept" and edge["event"].lower() in omitted_tags:
            omitted_targets.append({"from": edge["from"], "event": edge["event"],
                                    "targets": omitted_tags[edge["event"].lower()]})
    return {
        "status": "checked", "path": str(src.path), "sha256": hashlib.sha256(src.path.read_bytes()).hexdigest(),
        "source_actors": len(src.actors), "gameplay_actors": len(expected),
        "categories": dict(sorted(Counter(a.category for a in src.actors).items())),
        "missing": missing, "extra": extra, "property_differences": differences,
        "decode_errors": errors, "render_event_targets": omitted_targets,
        "conversion_notes": src.notes.counts,
    }


def markdown(reports: list[dict]) -> str:
    lines = ["# Map parity inventory", "",
             "Read-only source/conversion checks. Family counts overlap. Unmatched events are review candidates, "
             "not proof of a defect. This does not establish visual, audio, collision, multiplayer, or complete "
             "interactive parity; named easter eggs cannot be inferred from event identifiers alone.", "",
             "| Map | Actors | Movers | Lifts | Switches | Hack terminals | Items | Damageable props | Events | Unmatched | Issues | FX | Source |",
             "|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|"]
    for report in reports:
        f = report.get("families", {})
        source = report.get("source", {})
        source_status = source.get("status", "not requested")
        if source_status == "checked":
            problems = sum(len(source[k]) for k in ("missing", "extra", "property_differences", "decode_errors"))
            source_status = f"{problems} differences"
        cells = [report["map"], report.get("actors", 0), *(f.get(k, 0) for k in
                 ("movers", "lifts", "switches", "hackable_objectives", "items", "destructibles")),
                 report.get("events", 0), len(report.get("unmatched_events", [])), len(report["issues"]),
                 report.get("render_fx", "not checked"), source_status]
        lines.append("| " + " | ".join(map(str, cells)) + " |")
    lines += ["", "## Findings", ""]
    for report in reports:
        for issue in report["issues"]:
            lines.append(f"- {report['map']} / {issue['actor']}: {issue['kind']} — {issue['detail']}")
        source = report.get("source", {})
        for key in ("missing", "extra", "property_differences", "decode_errors", "render_event_targets"):
            if source.get(key):
                lines.append(f"- {report['map']}: {len(source[key])} source {key}; see JSON for identifiers.")
    lines += ["", "Detailed event candidates and source comparisons are in parity.json.", ""]
    return "\n".join(lines)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--content", type=Path, default=default_out_root())
    parser.add_argument("--game", type=Path, default=default_game_root())
    parser.add_argument("--source", action="store_true", help="compare against original local map packages")
    parser.add_argument("--maps", default="all")
    parser.add_argument("--out", type=Path, default=REPO_ROOT / "local" / "test-results" / "map-parity")
    args = parser.parse_args(argv)
    db = None
    if args.source:
        from ..sources.classdb import class_db  # noqa: PLC0415
        from .source import MapSource  # noqa: PLC0415
        db = class_db(str(args.game))
    wanted = None if args.maps == "all" else {x.casefold() for x in args.maps.split(",")}
    reports = []
    for folder in sorted((args.content / "svmap").iterdir()):
        if not folder.is_dir() or (wanted is not None and folder.name.casefold() not in wanted):
            continue
        if not (folder / "manifest.json").exists():
            continue
        try:
            gameplay = canonical.loads((folder / "sim" / "gameplay.json").read_bytes())
            classes = canonical.loads((folder / "sim" / "classes.json").read_bytes())["classes"]
            fx_path = folder / "render" / "fx.json"
            fx = canonical.loads(fx_path.read_bytes()) if fx_path.exists() else None
            lighting_path = folder / "render" / "lighting.json"
            lighting = canonical.loads(lighting_path.read_bytes()) if lighting_path.exists() else None
            report = audit_document(gameplay, classes, fx, lighting)
            for light in (lighting or {}).get("lights", []):
                png = light.get("sprite", {}).get("texture_uri")
                if png and not (folder / "render" / png).is_file():
                    report["issues"].append({"kind": "missing_sprite_texture", "actor": light["name"], "detail": png})
            if db is not None:
                from ..gamefiles import game_file  # noqa: PLC0415
                if game_file(args.game, f"Packages/Maps/{folder.name}.sdc").exists():
                    report["source"] = compare_source(MapSource(args.game, folder.name, db), gameplay)
                else:
                    report["source"] = {"status": "no source package"}
        except (OSError, ValueError, KeyError) as error:
            report = {"map": folder.name, "issues": [{"kind": "audit_error", "actor": "", "detail": str(error)}]}
        reports.append(report)
        print(f"{folder.name}: {len(report['issues'])} structural issues", flush=True)
    if wanted is not None:
        for missing in sorted(wanted - {r["map"].casefold() for r in reports}):
            reports.append({"map": missing, "issues": [{"kind": "missing_requested_map", "actor": "",
                            "detail": "No converted map manifest exists in the selected content root"}]})
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / "parity.json").write_text(json.dumps(reports, indent=2), encoding="utf-8")
    (args.out / "parity.md").write_text(markdown(reports), encoding="utf-8")
    print(f"Audited {len(reports)} maps: {args.out / 'parity.md'}")
    return int(not reports or any(r["issues"] or any(r.get("source", {}).get(k) for k in
               ("missing", "extra", "property_differences", "decode_errors")) for r in reports))


if __name__ == "__main__":
    raise SystemExit(main())
