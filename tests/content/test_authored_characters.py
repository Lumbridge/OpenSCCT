import json
from pathlib import Path

import pytest

from svmbuild.authored_characters import install_models, source_files
from svmbuild.writers.glb import pack_glb


def fixture(tmp_path):
    root = tmp_path / 'authoring'
    root.mkdir()
    (root / 'enabled.json').write_text('["spy"]')
    output = root / 'spy/export/ATT_01.glb'
    output.parent.mkdir(parents=True)
    target = tmp_path / 'chars/SPerso/ATT_01.glb'
    target.parent.mkdir(parents=True)
    document = {'asset': {'version': '2.0'}, 'nodes': [{'name': 'B'}],
                'skins': [{'joints': [0]}], 'accessors': [{'max': [1]}],
                'animations': [{'name': 'walk', 'samplers': [{'input': 0}]}],
                'extras': {'svmap': {'animation': 'PRO.anim.json'}}}
    target.write_bytes(pack_glb(document, b''))
    return root, output, target, document


def test_no_manifest_keeps_default_characters(tmp_path):
    assert install_models(tmp_path / 'chars', tmp_path) == []
    assert source_files(tmp_path) == []


def test_retimed_clip_rejected_before_install(tmp_path):
    root, output, target, doc = fixture(tmp_path)
    before = target.read_bytes()
    doc['accessors'][0]['max'] = [2]
    output.write_bytes(pack_glb(doc, b''))
    with pytest.raises(ValueError, match='retimed'):
        install_models(tmp_path / 'chars', root)
    assert target.read_bytes() == before


def test_skeleton_change_rejected(tmp_path):
    root, output, target, doc = fixture(tmp_path)
    doc['nodes'][0]['name'] = 'CTRL_root'
    output.write_bytes(pack_glb(doc, b''))
    with pytest.raises(ValueError, match='skeleton'):
        install_models(tmp_path / 'chars', root)


def test_texture_path_escape_rejected(tmp_path):
    root, output, target, doc = fixture(tmp_path)
    (output.parent.parent / 'private.png').write_bytes(b'not an asset')
    doc['images'] = [{'uri': '../private.png'}]
    output.write_bytes(pack_glb(doc, b''))
    with pytest.raises(ValueError, match='unsafe'):
        install_models(tmp_path / 'chars', root)


def test_compatible_export_and_texture_installed(tmp_path):
    root, output, target, doc = fixture(tmp_path)
    doc['images'] = [{'uri': 'textures/body.png'}]
    image = output.parent / 'textures/body.png'
    image.parent.mkdir()
    image.write_bytes(b'edited texture')
    output.write_bytes(pack_glb(doc, b''))
    assert image.resolve() in source_files(root)
    assert install_models(tmp_path / 'chars', root) == ['ATT_01.glb']
    assert target.read_bytes() == output.read_bytes()
    assert (target.parent / 'textures/body.png').read_bytes() == b'edited texture'
