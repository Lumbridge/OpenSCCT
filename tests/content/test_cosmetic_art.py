"""Original artwork must survive every fitted branch and content preparation."""
import json
import hashlib
from types import SimpleNamespace

import numpy as np
import pytest
from PIL import Image

from svmbuild import cosmetic_art, cosmetics, gunskin, skeleton


def test_all_catalog_artwork_is_present_readable_and_distinct():
    files=cosmetic_art.source_files()
    pictures=[path for path in files if path.suffix=='.png']
    assert {path.stem for path in pictures} == set(cosmetics.PATTERNS) | {'skeleton_back'}
    hashes=set()
    for path in pictures:
        pixels=cosmetic_art.load(path.stem)
        assert pixels.ndim==3 and pixels.shape[2]==3 and np.isfinite(pixels).all(), path.name
        digest=hashlib.sha256(path.read_bytes()).hexdigest()
        assert digest not in hashes, f'Duplicate cosmetic source artwork: {path.name}'
        hashes.add(digest)


def test_missing_authored_source_never_uses_procedural_art(tmp_path, monkeypatch):
    monkeypatch.setattr(cosmetic_art, 'SOURCE', tmp_path)
    with pytest.raises(FileNotFoundError, match='woodland.png'):
        cosmetics.pattern_image('woodland', 8, 8, np.ones((8, 8)), 'thumb')


def test_source_loader_is_exact_and_invalidates_replaced_art(tmp_path, monkeypatch):
    monkeypatch.setattr(cosmetic_art, 'SOURCE', tmp_path)
    (tmp_path/'textures').mkdir()
    path=tmp_path/'textures/woodland.png'
    Image.new('RGB',(256,256),(30,60,90)).save(path)
    first=cosmetic_art.load('woodland')
    np.testing.assert_allclose(first[0,0],np.array([30,60,90])/255)
    assert not first.flags.writeable
    Image.new('RGB',(256,256),(100,120,140)).save(path)
    np.testing.assert_allclose(cosmetic_art.load('woodland')[0,0],np.array([100,120,140])/255)


@pytest.mark.parametrize('pattern', list(cosmetics.PATTERNS))
def test_swatch_uses_original_art_for_every_pattern(monkeypatch, pattern):
    art=np.broadcast_to(np.array([.2,.4,.8],np.float32),(256,256,3))
    monkeypatch.setattr(cosmetic_art,'load',lambda name:art)
    output=cosmetics.pattern_image(pattern,16,16,np.zeros((16,16)),'thumb')
    np.testing.assert_allclose(output, np.broadcast_to([.2,.4,.8],output.shape), atol=1/255)


@pytest.mark.parametrize('pattern', ['woodland','desert','galaxy','inferno','golden','mastercraft','pyre','kaido','cryo'])
def test_special_suit_branches_sample_rgb_art(monkeypatch, pattern):
    art=np.broadcast_to(np.array([.17,.43,.79],np.float32),(16,16,3))
    monkeypatch.setattr(cosmetic_art,'load',lambda name:art)
    y,x=np.mgrid[:8,:8].astype(np.float32)/8
    points=np.stack([x,y,x*0],-1)
    normals=np.zeros_like(points);normals[...,2]=1
    layout={'P':points,'N':normals,'cover':np.ones((8,8),bool),'group':np.full((8,8),skeleton.TORSO)}
    monkeypatch.setattr(gunskin,'bake',lambda *args:layout)
    mesh=SimpleNamespace(pos=points.reshape(-1,3))
    shape,palette=cosmetics.PATTERNS[pattern]
    output=gunskin.draw_suit(pattern,shape,palette,mesh,'body',8,8,np.zeros((8,8)))
    np.testing.assert_allclose(output,np.broadcast_to(art[0,0],output.shape),atol=1e-6)


def test_skeleton_selects_anatomy_by_group_without_tiling(tmp_path,monkeypatch):
    monkeypatch.setattr(cosmetic_art,'SOURCE',tmp_path)
    # Distinct source regions stand in for the skull and torso; no interpolation
    # across their boundary can turn either part into the other's artwork.
    art=np.zeros((64,64,3),np.float32);art[:32,:,0]=1;art[32:,:,1]=1
    back=np.zeros_like(art);back[:32,:,2]=1;back[32:,:,:2]=1
    monkeypatch.setattr(cosmetic_art,'load',lambda name:back if name=='skeleton_back' else art)
    regions={'head':[0,0,1,.45],'torso':[0,.55,1,1]}
    (tmp_path/'skeleton-layout.json').write_text(json.dumps({'regions':regions,'back_regions':regions}))
    pos=np.array([[0,0,0],[1,1,0],[0,0,0],[1,1,0]],np.float32)
    groups=np.zeros((4,10));groups[:2,skeleton.HEAD]=1;groups[2:,skeleton.TORSO]=1
    mesh=SimpleNamespace(pos=pos,groups=groups)
    layout={'P':np.array([[[.5,.5,0]]*4],np.float32),
            'N':np.array([[[0,0,1],[0,0,1],[0,0,-1],[0,0,-1]]],np.float32),
            'group':np.array([[skeleton.HEAD,skeleton.TORSO]*2]),'cover':np.ones((1,4),bool)}
    monkeypatch.setattr(skeleton,'bake',lambda *args:layout)
    output=cosmetic_art.skeleton_image(mesh,'body',1,4)
    np.testing.assert_allclose(output[0],[[1,0,0],[0,1,0],[0,0,1],[1,1,0]])
