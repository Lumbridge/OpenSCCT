"""Damage materials and UE section identities must survive map conversion."""
import builtins
import io
import json
import struct
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import pytest

from svmbuild.paths import default_out_root, import_scctpkg


def read_glb(path):
    data = path.read_bytes()
    size = struct.unpack_from('<I', data, 12)[0]
    return json.loads(data[20:20 + size])


def test_empty_mesh_sections_keep_original_skin_slot(tmp_path, monkeypatch):
    # The mesh helper imports actor metadata, but this fixture never extracts actors or needs game data.
    original_open = builtins.open

    def open_for_mesh_helper(file, *args, **kwargs):
        if isinstance(file, (str, Path)):
            path = Path(file)
            if path.name == 'class_defaults.json' and path.parent.name == 'scctpkg':
                return io.StringIO('{}')
        return original_open(file, *args, **kwargs)

    monkeypatch.setattr(builtins, 'open', open_for_mesh_helper)
    import_scctpkg()
    from scctpkg.export_gltf import GltfWriter, _static_mesh_prims
    mesh = SimpleNamespace(
        name='test', positions=np.zeros((3, 3), np.float32), normals=None, uvs=[],
        indices=np.array([0, 1, 2], np.uint32), sphere=(10., 20., 30., 40.),
        sections=[SimpleNamespace(num_faces=0), SimpleNamespace(num_faces=1, first_index=0, material_ref=0)],
    )
    writer = GltfWriter()
    prims = _static_mesh_prims(mesh, SimpleNamespace(path=Path('Test.usx')), None, writer, tmp_path, {})
    index = writer.mesh('test', prims)
    assert len(writer.meshes[index]['primitives']) == 1
    assert writer.meshes[index]['primitives'][0]['extras']['scctSection'] == 1
    assert writer.meshes[index]['extras']['scctSphere'] == [.1, .3, .2, .4]


def test_converted_damage_and_trigger_skins_have_materials_and_textures():
    maps = list((default_out_root() / 'svmap').glob('*/sim/gameplay.json'))
    if not maps:
        pytest.skip('converted maps absent')
    checked = 0
    for path in maps:
        render = path.parents[1] / 'render'
        if not (render / 'materials.json').exists():
            continue
        document = json.loads((render / 'materials.json').read_text())
        materials = document['materials']
        sources = {entry.get('source'): entry for entry in materials.values()}
        for actor in json.loads(path.read_text())['actors']:
            props = actor['props']
            skins = [props.get('TriggeredSkin')]
            skins.extend(stage.get('Skin') for stage in props.get('DamagedMeshes', []) if isinstance(stage, dict))
            for skin in skins:
                if not isinstance(skin, dict) or not skin.get('$asset'):
                    continue
                source = skin['$asset']
                assert source in sources, (path.parents[1].name, actor['id'], source)
                texture = sources[source]['flags'].get('texture')
                if texture:
                    assert (render / texture).is_file(), source
                else:
                    assert document['graph'][source]['class'] == 'Engine.ConstantColor', source
                checked += 1
    assert checked > 0
