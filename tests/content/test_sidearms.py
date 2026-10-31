"""Sidearm source provenance, neutral rigs and reproducible first-person assembly."""
from pathlib import Path
import hashlib
import shutil
import numpy as np

import pytest

from svmbuild.sidearms import SOURCE, IDS, install_audio
from svmbuild.sidearm_models import assemble, read, values
from svmbuild.pistol_arms import pose, tracks


def test_pistol_exports_have_credited_sources_external_textures_and_valid_rigs():
    credits=(SOURCE/'CREDITS.txt').read_text(encoding='utf-8')
    for author in ['Joche007','IVA','Neerhom_mod','ELIZION','DJMaesen']:
        assert author in credits
    assert 'CC-BY-4.0' in credits
    assert not list(SOURCE.glob('*_first.glb')), 'Retargeted retail clips must remain in the content output.'
    arms,_=read(SOURCE/'arms_base.glb')
    joints={arms['nodes'][i]['name'] for i in arms['skins'][0]['joints']}
    assert 'R_point3_035' in joints and 'L_thumb3_06' in joints
    assert 'CC-BY-4.0' in (SOURCE.parent/'sources/arms/license.txt').read_text()
    for pistol in IDS:
        license_file=SOURCE.parent/'sources'/pistol/'license.txt'
        assert 'CC-BY-4.0' in license_file.read_text()
        model,data=read(SOURCE/(pistol+'.glb'))
        assert all((SOURCE/image['uri']).is_file() for image in model['images'])
        for node in model['nodes']:
            if 'mesh' not in node:continue
            skin=model['skins'][node['skin']]
            assert {model['nodes'][i]['name'] for i in skin['joints']} >= {'corps','pompe','chargeur'}
            for primitive in model['meshes'][node['mesh']]['primitives']:
                joints=values(model,data,primitive['attributes']['JOINTS_0'])
                assert joints.min() >= 0 and joints.max() < len(skin['joints'])


def test_assembly_is_reproducible_and_keeps_contextual_arm_clips(tmp_path):
    reference=Path(__file__).resolve().parents[2]/'.content/shared/chars/SWeaponFirst/1DEF_01.glb'
    if not reference.is_file():pytest.skip('Converted merc arms are required.')
    (tmp_path/'SWeaponFirst').mkdir()
    shutil.copy2(reference,tmp_path/'SWeaponFirst/1DEF_01.glb')
    shutil.copytree(SOURCE,tmp_path/'OpenScctSidearms')
    for pistol in IDS:
        assemble(tmp_path,pistol)
        first=tmp_path/'OpenScctSidearms'/(pistol+'_first.glb')
        digest=hashlib.sha256(first.read_bytes()).digest()
        assemble(tmp_path,pistol)
        assert hashlib.sha256(first.read_bytes()).digest()==digest
        model,data=read(first)
        clips={clip['name'] for clip in model['animations']}
        assert {'FirstReloStFd2','FirstButtStFd2','FirstShooStFd2'} <= clips
        idle=next(a for a in model['animations'] if a['name']=='FirstWaitStFd2')
        world=pose(model,tracks(model,data,idle),.5)
        skin=model['skins'][0]
        binds=values(model,data,skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
        handle=[]
        for node in model['nodes']:
            if node.get('name')!=pistol+'_parts':continue
            for primitive in model['meshes'][node['mesh']]['primitives']:
                xyz=values(model,data,primitive['attributes']['POSITION'])
                joints=values(model,data,primitive['attributes']['JOINTS_0'])[:,0]
                for joint in np.unique(joints):
                    m=world[skin['joints'][joint]]@binds[joint];vertices=xyz[joints==joint]
                    handle.extend((m[:3,:3]@vertices.T).T+m[:3,3])
        handle=np.asarray(handle)
        for finger in ['R_middle3_039','R_ring3_044','R_pink3_048']:
            node=next(i for i,n in enumerate(model['nodes']) if n.get('name')==finger)
            assert np.linalg.norm(handle-world[node][:3,3],axis=1).min() < .012, f'{pistol}: {finger} floats off the grip'
        for mesh in model['meshes']:
            for primitive in mesh['primitives']:
                joints=values(model,data,primitive['attributes']['JOINTS_0'])
                assert joints.min() >= 0 and joints.max() < len(model['skins'][0]['joints'])


def test_audio_exports_are_reproducible_and_cover_every_pistol(tmp_path):
    install_audio(tmp_path)
    paths=list((tmp_path/'wav/OpenScctSidearms').glob('*.wav'))
    assert len(paths)==8
    digests={p.name:hashlib.sha256(p.read_bytes()).digest() for p in paths}
    install_audio(tmp_path)
    assert {p.name:hashlib.sha256(p.read_bytes()).digest() for p in paths}==digests
