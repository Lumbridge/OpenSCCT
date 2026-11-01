"""Synthetic regressions for the read-only parity audit, independent of game files."""

from types import SimpleNamespace

from svmbuild.mapconv.audit import ancestry, audit_document, compare_source, main, markdown
from svmbuild.svmap import with_derived


def scene(actors=(), animations=None):
    return with_derived({
        "formatVersion": "1.1.0", "map": "Synthetic",
        "levelInfo": {"id": "Level0", "class": "Engine.LevelInfo", "props": {"Tag": "LevelInfo"}},
        "actors": list(actors), "animations": animations or {},
    })


def actor(key, cls, **props):
    return {"id": key, "class": cls, "props": {"Tag": key, **props}}


CLASSES = {
    "Engine.LevelInfo": {}, "Engine.Mover": {}, "Engine.Trigger": {}, "Engine.Emitter": {},
    "SBase.SLift": {"super": "Engine.Mover"}, "SBase.SAnimatedMesh": {},
}


def test_inherited_families_and_unmatched_events_are_not_failures():
    doc = scene([actor("Lift0", "SBase.SLift"), actor("Trigger0", "Engine.Trigger", Event="unused")])
    report = audit_document(doc, CLASSES)
    assert report["issues"] == []
    assert report["families"]["movers"] == report["families"]["lifts"] == 1
    assert [e["event"] for e in report["unmatched_events"]] == ["unused"]


def test_reports_missing_reference_and_class_even_when_indexes_are_consistent():
    doc = scene([actor("Unknown0", "Custom.Unported", Owner={"$actor": "Absent0"})])
    report = audit_document(doc, CLASSES)
    assert {i["kind"] for i in report["issues"]} == {"missing_class", "missing_actor_reference"}


def test_reports_corrupt_indexes_and_duplicate_actor_identity():
    doc = scene([actor("Lift0", "SBase.SLift"), actor("Lift0", "SBase.SLift")])
    doc["tagIndex"] = {}
    doc["eventEdges"] = [{"from": "Lift0", "event": "invented", "via": "Event", "to": []}]
    assert {i["kind"] for i in audit_document(doc, CLASSES)["issues"]} == {
        "duplicate_actor", "tag_index_mismatch", "event_index_mismatch",
    }


def test_animation_lookup_is_case_insensitive_and_checks_missing_sequence():
    doc = scene([actor("Animated0", "SBase.SAnimatedMesh", Mesh={"$asset": "synthetic.MESH"},
                       Animations=["BREAK", "Missing"])],
                {"Synthetic.Mesh": {"sequences": {"break": {"frames": 2, "rate": 1, "notifies": []}}}})
    report = audit_document(doc, CLASSES)
    assert report["issues"] == [{"kind": "missing_animation", "actor": "Animated0", "detail": "Missing"}]


def test_rendered_map_must_supply_each_emitter_but_sim_only_audit_is_allowed():
    doc = scene([actor("Emitter0", "Engine.Emitter")])
    assert not audit_document(doc, CLASSES)["issues"]
    assert audit_document(doc, CLASSES, {"emitters": {}})["issues"][0]["kind"] == "missing_emitter"
    assert not audit_document(doc, CLASSES, {"emitters": {"Emitter0": {}}})["issues"]


def test_damageable_counts_respect_class_defaults_and_per_actor_overrides():
    classes = {**CLASSES, "SBase.SGamePlayObject": {"defaults": {"bDamageable": True}}}
    doc = scene([actor("Breakable0", "SBase.SGamePlayObject"),
                 actor("Solid0", "SBase.SGamePlayObject", bDamageable=False)])
    families = audit_document(doc, classes)["families"]
    assert families["gameplay_props"] == 2
    assert families["destructibles"] == 1


def test_visible_sprite_light_needs_export_even_when_light_type_is_none():
    light = {"name": "Light0", "hidden": False, "ltype": 0,
             "props": {"bHidden": False, "Texture": {"$asset": "Synthetic.Flare"}}}
    report = audit_document(scene(), CLASSES, lighting={"lights": [light]})
    assert report["families"]["light_sprites"] == 1
    assert report["issues"][0]["kind"] == "missing_light_sprite"
    light["sprite"] = {"texture_uri": "textures/Synthetic_Flare.png"}
    assert not audit_document(scene(), CLASSES, lighting={"lights": [light]})["issues"]


def test_hidden_lights_and_coronas_do_not_require_an_extra_sprite():
    # The light illuminates the scene (outer hidden=false), but its Actor sprite is hidden by class default.
    hidden = {"name": "Hidden0", "hidden": False, "props": {"Texture": {"$asset": "Synthetic.Flare"}}}
    corona = {"name": "Corona0", "hidden": False, "corona": {"texture_uri": "textures/Flare.png"},
              "props": {"bHidden": False, "Texture": {"$asset": "Synthetic.Flare"}}}
    report = audit_document(scene(), CLASSES, lighting={"lights": [hidden, corona]})
    assert not report["issues"]
    assert report["families"].get("light_sprites", 0) == 0


def test_cyclic_class_metadata_cannot_hang_audit_and_report_states_limits():
    assert ancestry("A", {"A": {"super": "B"}, "B": {"super": "A"}}) == {"A", "B"}
    report = markdown([audit_document(scene(), CLASSES)])
    assert "not requested" in report
    assert "does not establish" in report


def test_actor_references_and_duplicates_follow_case_insensitive_runtime_identity():
    doc = scene([actor("Lift0", "SBase.SLift", Owner={"$actor": "level0"}), actor("LIFT0", "SBase.SLift")])
    assert [i["kind"] for i in audit_document(doc, CLASSES)["issues"]] == ["duplicate_actor"]


def test_explicit_missing_map_fails_instead_of_silently_claiming_coverage(tmp_path):
    (tmp_path / "svmap").mkdir()
    assert main(["--content", str(tmp_path), "--maps", "Missing", "--out", str(tmp_path / "report")]) == 1
    assert "missing_requested_map" in (tmp_path / "report" / "parity.json").read_text()


def test_source_comparison_catches_omitted_actors_and_changed_properties(tmp_path, monkeypatch):
    from svmbuild.mapconv import gameplay as converter

    source_file = tmp_path / "source.synthetic"
    source_file.write_bytes(b"synthetic fixture")
    originals = [SimpleNamespace(name="Level0", category="level", error=""),
                 SimpleNamespace(name="Lift0", category="gameplay", error=""),
                 SimpleNamespace(name="Omitted0", category="gameplay", error="")]
    source_doc = scene([actor("Lift0", "SBase.SLift", MoveTime=2.0), actor("Omitted0", "Engine.Trigger")])
    original_json = {a["id"]: a for a in [source_doc["levelInfo"], *source_doc["actors"]]}
    source_doc["levelInfo"]["props"]["GEActivationMask"] = 0
    monkeypatch.setattr(converter, "actor_json", lambda _src, info: original_json[info.name])
    src = SimpleNamespace(path=source_file, actors=originals, of=lambda *_args: originals,
                          value=lambda _actor, _prop, default: default,
                          notes=SimpleNamespace(counts={}))
    converted = scene([actor("Lift0", "SBase.SLift", MoveTime=3.0), actor("Extra0", "Engine.Trigger")])
    converted["levelInfo"]["props"]["GEActivationMask"] = 0
    report = compare_source(src, converted)
    assert report["missing"] == ["Omitted0"]
    assert report["extra"] == ["Extra0"]
    assert report["property_differences"] == [{"actor": "Lift0", "property": "MoveTime"}]
    assert len(report["sha256"]) == 64
