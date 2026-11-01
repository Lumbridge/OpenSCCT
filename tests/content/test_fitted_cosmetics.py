"""Geometry-backed cosmetic regressions: no flat-atlas fallback or cross-material paint."""
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

from svmbuild import cosmetics, skeleton
from svmbuild.fitted import SurfaceMesh, coverage, body_group
from svmbuild.sidearm_models import append, write


def test_authored_reader_keeps_separate_buffers_and_node_transforms(tmp_path):
    doc = dict(nodes=[], meshes=[], materials=[], images=[], textures=[], accessors=[], bufferViews=[])
    binary = bytearray()
    for i in range(2):
        p = append(doc, binary, [[0,0,0],[1,0,0],[0,1,0]], 'VEC3')
        n = append(doc, binary, [[0,0,1]]*3, 'VEC3')
        uv = append(doc, binary, [[0,0],[1,0],[0,1]], 'VEC2')
        ix = append(doc, binary, [[0],[1],[2]], 'SCALAR', 5123)
        doc['images'].append({'uri':f'textures/part{i}.png'})
        doc['textures'].append({'source':i})
        doc['materials'].append({'pbrMetallicRoughness':{'baseColorTexture':{'index':i}}})
        doc['meshes'].append({'primitives':[{'attributes':{'POSITION':p,'NORMAL':n,'TEXCOORD_0':uv},'indices':ix,'material':i}]})
        doc['nodes'].append({'mesh':i,'translation':[i*3,0,0]})
    path = tmp_path/'parts.glb';write(path,doc,binary)
    mesh = SurfaceMesh(path)
    assert len(mesh.pos) == 6
    np.testing.assert_allclose(mesh.pos[3], [3,0,0])
    np.testing.assert_array_equal(mesh.tris['part1'][0], [[3,4,5]])
    assert skeleton.bake(mesh,'part1',16,16)['cover'].any()


def test_authored_hand_joints_are_not_sleeves():
    assert body_group('L_forearm_02') == skeleton.ARM_L
    assert body_group('R_elbow_026') == skeleton.ARM_R
    assert body_group('L_thumb1_04') == skeleton.HAND_L
    assert body_group('R_wrist_028') == skeleton.HAND_R


def test_missing_mesh_fails_instead_of_painting_flat(tmp_path):
    src=tmp_path/'base.png';Image.new('RGBA',(16,16),'gray').save(src)
    with pytest.raises(ValueError,match='without its mesh'):
        cosmetics._paint_job((str(src),str(tmp_path/'out.png'),'woodland','weapon',(16,16),None))
    assert not (tmp_path/'out.png').exists()


def test_paint_preserves_uncovered_texels_and_alpha(monkeypatch):
    from svmbuild import fitted
    mask=np.zeros((8,8),np.float32);mask[2:6,2:6]=1
    monkeypatch.setattr(fitted,'coverage',lambda *args:mask)
    monkeypatch.setattr(fitted,'coordinated_parts',lambda colours,*args:colours)
    monkeypatch.setattr(cosmetics,'pattern_image',lambda *args:np.ones((8,8,3),np.float32))
    original=np.full((8,8,4),63,np.uint8)
    out=cosmetics.paint(original,'woodland','weapon',object())
    np.testing.assert_array_equal(out[mask==0],original[mask==0])
    np.testing.assert_array_equal(out[...,3],original[...,3])
    assert np.any(out[mask==1,:3]!=original[mask==1,:3])


def test_all_authored_sidearm_materials_have_geometry(repo_root):
    from svmbuild.sidearms import IDS, SOURCE
    for name in IDS:
        mesh=SurfaceMesh(SOURCE/(name+'.glb'))
        for stem in mesh.tris:
            layout=skeleton.bake(mesh,stem,32,32)
            assert layout['cover'].any(), (name,stem)
            source=SOURCE/'textures'/(stem+'.png')
            assert source.exists()


def test_fitted_tints_match_existing_account_colours(repo_root):
    import re
    catalog=(repo_root/'src/OpenScct.Net/Cosmetics/CosmeticCatalog.cs').read_text()
    table=catalog.split('[] s_tints =',1)[1].split('];',1)[0]
    expected={int(rgb,16) for rgb in re.findall(r'0x([0-9A-Fa-f]{6})',table)}
    assert set(cosmetics._TINT_RGB)==expected
    for package,stem,patterns in cosmetics.TARGETS:
        if stem in ('SPersoTextures_Shadw_agent','SPersoTextures_Shadw_agent_2',
                    'SPersoTextures_DEF_01_A_Body','SWeaponTex_1DEF_01'):
            assert set(cosmetics._TINTS)<=set(patterns)


def test_gear_is_preserved_and_hand_finish_stays_nonmetallic(monkeypatch):
    from svmbuild import fitted
    h=w=32
    group=np.full((h,w),skeleton.TORSO)
    group[:4]=skeleton.HAND_L
    monkeypatch.setattr(skeleton,'bake',lambda *args:{'cover':np.ones((h,w),bool),'group':group})
    original=np.full((h,w,4),128,np.uint8)
    from types import SimpleNamespace
    mesh=SimpleNamespace(joints={})
    mask=fitted.coverage(original,mesh,'SPersoTextures_Shadw_agent','woodland')
    assert np.all(mask[:4]==1)
    lighting=fitted.coverage(original,mesh,'SPersoTextures_Shadw_agent','woodland',lighting=True)
    assert np.all(lighting[:4]==0)
    assert mask[20,20]==0  # equipment atlas region, independent of skinning weights
    assert mask[10,10]>0   # garment panel is paintable


def test_boot_shaft_is_included_even_when_weighted_to_calf():
    from types import SimpleNamespace
    from svmbuild.fitted import secondary_parts
    mesh=SimpleNamespace(joints={'B L Foot':np.array([0,.1,0]),'B L Calf':np.array([0,.6,0])})
    layout={'cover':np.ones((1,3),bool),'group':np.array([[skeleton.FOOT_L,skeleton.LEG_L,skeleton.LEG_L]]),
            'P':np.array([[[0,.05,0],[0,.3,0],[0,.55,0]]])}
    np.testing.assert_array_equal(secondary_parts(mesh,layout)[skeleton.FOOT_L],[[True,True,False]])


def test_secondary_rebake_matches_full_bake(tmp_path):
    from svmbuild.fitted import refinish_parts
    from svmbuild.sidearms import SOURCE
    stem='OpenScctSidearms_magnum44_Taurus_baseColor'
    job=(str(SOURCE/'textures'/(stem+'.png')),str(tmp_path/'gold.png'),'golden',stem,(64,64),str(SOURCE/'magnum44.glb'))
    cosmetics._paint_job(job)
    before=np.array(Image.open(job[1]))
    refinish_parts(job)
    np.testing.assert_array_equal(np.array(Image.open(job[1])),before)


@pytest.mark.parametrize('pattern',['woodland','galaxy','inferno'])
def test_head_boots_and_gloves_receive_a_motif_not_a_solid_colour(monkeypatch,pattern):
    from types import SimpleNamespace
    from svmbuild.fitted import coordinated_parts
    size=48
    y,x=np.mgrid[:size,:size].astype(np.float32)/(size-1)
    points=np.stack([x,y,np.zeros_like(x)],-1)
    group=np.where(y>.66,skeleton.HEAD,np.where(y<.33,skeleton.FOOT_L,skeleton.HAND_L))
    normal=np.zeros_like(points);normal[...,2]=1
    layout={'cover':np.ones((size,size),bool),'group':group,'P':points,'N':normal}
    monkeypatch.setattr(skeleton,'bake',lambda *args:layout)
    retail=np.full((size,size,4),128,np.uint8)
    out=coordinated_parts(np.zeros((size,size,3),np.float32),retail,SimpleNamespace(joints={}),
                          'SPersoTextures_Shadw_agent',pattern)
    for part in (skeleton.HEAD,skeleton.FOOT_L,skeleton.HAND_L):
        pixels=out[group==part]
        chroma=pixels/np.maximum(pixels.sum(1,keepdims=True),1e-6)
        assert chroma.std(0).max()>.02, (pattern,part,'solid colour substituted for motif')
