"""Synthetic per-actor modulation streams; no game packages or converted data."""
import builtins
import io
import json
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest


@pytest.fixture
def scene(monkeypatch, tmp_path):
    from svmbuild.paths import import_scctpkg
    original_open = builtins.open

    def synthetic_defaults(file, *args, **kwargs):
        if isinstance(file, (str, Path)):
            path = Path(file)
            if path.name == 'class_defaults.json' and path.parent.name == 'scctpkg' and not path.exists():
                return io.StringIO('{}')
        return original_open(file, *args, **kwargs)

    monkeypatch.setattr(builtins, 'open', synthetic_defaults)
    import_scctpkg()
    from scctpkg import export_gltf as export
    from scctpkg.level import ActorRecord
    from scctpkg.staticmesh import Section, StaticMesh
    from scctpkg.materials import ResolvedMaterial

    mesh = StaticMesh('Beam', np.array([[0, 0, 0], [1, 0, 0], [0, 1, 0]], np.float32),
        np.array([[0, 0, 1]] * 3, np.float32), [np.array([[0, 0], [1, 0], [0, 1]], np.float32)],
        np.full((3, 4), 128, np.uint8), np.array([0, 1, 2], np.uint16),
        [Section(0, 0, 2, 1, material_ref=1)], (0, 0, 0), (1, 1, 0), {'UseVertexColor': True})
    records = [ActorRecord(name, 'StaticMeshActor', 'staticmesh', location=(i * 10, 0, 0),
        mesh_key='Fixture.Beam', mesh_ref=1, extra={'smi': 2, 'ambient_glow': 200}) for i, name in enumerate(['A', 'B', 'C'])]
    package = SimpleNamespace(path=tmp_path/'Fixture.sdc')
    material = ResolvedMaterial('Fixture.Add', None, additive=True)
    monkeypatch.setattr(export.Package, 'load', staticmethod(lambda _: package))
    monkeypatch.setattr(export, 'MaterialResolver', lambda _: SimpleNamespace(resolve=lambda *_: material))
    monkeypatch.setattr(export, 'read_static_mesh', lambda *_: mesh)
    monkeypatch.setattr(export, 'extract_level', lambda *_: records)
    monkeypatch.setattr(export, 'extract_look', lambda _: {})
    monkeypatch.setattr(export, '_export_emitters', lambda *_: [])
    monkeypatch.setattr(export, '_read_color_stream', lambda *_: pytest.fail('Authored selection reached PC baked-color path'))
    monkeypatch.setattr(export, '_fold_vertex_color_glow', lambda *_: pytest.fail('Authored selection folded PC ambient glow'))
    pset = SimpleNamespace(resolve=lambda *_: (package, SimpleNamespace(class_name='StaticMesh')))

    def override(data, **changes):
        args = dict(mesh_key='Fixture.Beam', vertex_sha256=export.authored_color_vertex_identity(mesh), rgba=data)
        return export.AuthoredColorOverride(**(args | changes))

    def run(overrides=None, name='out'):
        export.export_map(package.path, pset, tmp_path/name, include_bsp=False, authored_color_overrides=overrides)
        folder = tmp_path/name/'Fixture'
        return json.loads((folder/'scene.json').read_text()), json.loads((folder/'Fixture.gltf').read_text()), (folder/'Fixture.bin').read_bytes()

    return SimpleNamespace(export=export, mesh=mesh, records=records, material=material, override=override, run=run)


def colors_of(doc, binary, name):
    mesh = next(m for m in doc['meshes'] if m['name'] == name)
    accessor = doc['accessors'][mesh['primitives'][0]['attributes']['COLOR_0']]
    view = doc['bufferViews'][accessor['bufferView']]
    start = view.get('byteOffset', 0) + accessor.get('byteOffset', 0)
    return binary[start:start + accessor['count'] * 4]


def test_pc_default_still_prefers_shared_authored_colors_over_smi_and_empty_option_is_identical(scene):
    default = scene.run()
    explicit = scene.run({}, 'empty')
    assert default == explicit
    actors, doc, binary = default
    assert len({a['mesh'] for a in actors['actors']}) == 1
    for actor in actors['actors']:
        assert actor['vertex_lit'] and actor['authored_vc']
        assert colors_of(doc, binary, actor['mesh']) == bytes([128] * 12)


def test_same_mesh_gets_distinct_per_actor_variants_without_ambient_fold(scene):
    first = bytes([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])
    second = bytes([12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1])
    scene.records[0].extra['unlit'] = True
    actors, doc, binary = scene.run({'A': scene.override(first), 'B': scene.override(second)})
    assert len({a['mesh'] for a in actors['actors']}) == 3
    for actor, expected in zip(actors['actors'], [first, second, bytes([128] * 12)]):
        assert colors_of(doc, binary, actor['mesh']) == expected
        assert actor['authored_vc'] and actor['vertex_lit']
        assert actor['ambient_glow'] == 200  # source field retained; authored overlay ignores it
    assert [a['location'] for a in actors['actors']] == [[0, 0, 0], [10, 0, 0], [20, 0, 0]]
    assert len(actors['authored_vc_meshes']) == 3


def test_identical_override_colors_share_a_variant(scene):
    value = scene.override(bytes(range(12)))
    actors, _, _ = scene.run({'A': value, 'B': value})
    assert actors['actors'][0]['mesh'] == actors['actors'][1]['mesh']
    assert actors['actors'][2]['mesh'] != actors['actors'][0]['mesh']


@pytest.mark.parametrize('change,match', [
    ({'mesh_key': 'Other.Beam'}, 'mesh identity'),
    ({'vertex_sha256': '0' * 64}, 'vertex identity'),
    ({'rgba': b'\0' * 8}, 'four RGBA'),
    ({'rgba': bytearray(12)}, 'four RGBA'),
])
def test_bad_attribution_or_payload_fails_closed(scene, change, match):
    with pytest.raises(ValueError, match=match):
        scene.run({'A': scene.override(bytes(12), **change)})


def test_missing_or_hidden_actor_is_not_silently_ignored(scene):
    with pytest.raises(ValueError, match='one visible mesh actor'):
        scene.run({'Missing': scene.override(bytes(12))})
    scene.records[0].hidden = True
    with pytest.raises(ValueError, match='one visible mesh actor'):
        scene.run({'A': scene.override(bytes(12))})


def test_vertex_identity_includes_uv_order_and_normals(scene):
    identity = scene.export.authored_color_vertex_identity(scene.mesh)
    scene.mesh.uvs[0][[0, 1]] = scene.mesh.uvs[0][[1, 0]]
    assert scene.export.authored_color_vertex_identity(scene.mesh) != identity
    scene.mesh.uvs[0][[0, 1]] = scene.mesh.uvs[0][[1, 0]]
    scene.mesh.normals[0] *= -1
    assert scene.export.authored_color_vertex_identity(scene.mesh) != identity


def test_non_additive_override_is_rejected(scene):
    scene.material.additive = False
    with pytest.raises(ValueError, match='additive section'):
        scene.run({'A': scene.override(bytes(12))})


@pytest.mark.parametrize('empty', [False, True])
def test_override_emission_failure_never_returns_a_partial_export(scene, monkeypatch, tmp_path, empty):
    original = scene.export._static_mesh_prims
    calls = 0

    def fail_variant(*args, **kwargs):
        nonlocal calls
        calls += 1
        if calls == 2:
            if empty:
                return []
            raise RuntimeError('synthetic mesh failure')
        return original(*args, **kwargs)

    monkeypatch.setattr(scene.export, '_static_mesh_prims', fail_variant)
    with pytest.raises(ValueError, match='A: authored color override mesh emission failed'):
        scene.run({'A': scene.override(bytes(12))})
    assert not (tmp_path/'out/Fixture/Fixture.gltf').exists()
    assert not (tmp_path/'out/Fixture/scene.json').exists()
