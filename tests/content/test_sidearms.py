"""Sidearm source provenance, neutral rigs and reproducible first-person assembly."""
from pathlib import Path
import hashlib
import shutil
import wave
import json
import math
import numpy as np

import pytest

from svmbuild.sidearms import SOURCE, IDS, install_audio
from svmbuild.sidearm_models import assemble, read, values
from svmbuild.pistol_arms import pose, tracks, sample


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
        license_file=SOURCE.parent/'sources'/('suppressed' if pistol=='unsuppressed' else pistol)/'license.txt'
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
    primary,primary_data=read(reference)
    primary_hand=next(i for i,n in enumerate(primary['nodes']) if n.get('name')=='Bip01 R Hand')
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
        fx=next(i for i,n in enumerate(model['nodes']) if n.get('name')=='FX')
        # The authored barrel points forward (+X), with the small inward framing yaw.
        # A translation-only FX inherited the grip's sideways axis and sent the laser left.
        direction=world[fx][:3,0];direction=direction/np.linalg.norm(direction)
        assert direction @ np.array([1.,0.,0.]) > .97, f'{pistol}: sideways muzzle axis {direction}'
        reload_clip=next(a for a in model['animations'] if a['name']=='FirstReloStFd2')
        reload_tracks=tracks(model,data,reload_clip)
        duration=max(float(t[-1]) for _,_,t,_ in reload_tracks)
        right_hand=next(i for i,n in enumerate(model['nodes']) if n.get('name','').startswith('R_wrist_'))
        gun=next(i for i,n in enumerate(model['nodes']) if n.get('name')=='pistol')
        assert gun in model['nodes'][right_hand].get('children',[]), 'Weapon must inherit the firing wrist in every blend'
        for movement in ('FirstWaitStFd2','FirstWalkStFd2','FirstJumpStFd2','FirstLandStFd2'):
            source_tracks=tracks(primary,primary_data,next(a for a in primary['animations'] if a['name']==movement))
            target_tracks=tracks(model,data,next(a for a in model['animations'] if a['name']==movement))
            end=max(float(t[-1]) for _,_,t,_ in source_tracks)
            # Compare baked sample times; interpolating between them adds a
            # second approximation to the primary's own sparse animation keys.
            times=np.linspace(0,end,max(2,math.ceil(end*30)+1))
            source_y=np.array([pose(primary,source_tracks,t)[primary_hand][1,3] for t in times])
            target_y=np.array([pose(model,target_tracks,t)[right_hand][1,3] for t in times])
            # glTF's up axis is Y on both rigs; preserve the whole vertical
            # trajectory rather than only checking that the weapon moves.
            np.testing.assert_allclose(target_y-target_y[0],source_y-source_y[0],atol=.0001,
                err_msg=f'{pistol}: {movement} vertical motion was retargeted sideways')
            for side in ('L','R'):
                chain=[next(i for i,n in enumerate(model['nodes']) if n.get('name','').startswith(side+'_'+part+'_'))
                       for part in ('arm','elbow','wrist')]
                neutral=pose(model,tracks(model,data,idle),0)
                def elbow_angle(frame):
                    a,b,c=[frame[i][:3,3] for i in chain]
                    upper=a-b;lower=c-b
                    return np.dot(upper,lower)/(np.linalg.norm(upper)*np.linalg.norm(lower))
                for phase in (.1,.5,.9):
                    assert abs(elbow_angle(pose(model,target_tracks,phase*end))-elbow_angle(neutral))<.001, \
                        f'{pistol}: {movement} distorts the {side} elbow'
        # Movement is applied first, then the full-weight reload. Sparse action
        # tracks previously left jump/landing transforms on the wrist ancestors.
        for movement in ('FirstJumpStFd2','FirstLandStFd2'):
            movement_clip=next(a for a in model['animations'] if a['name']==movement)
            movement_tracks=tracks(model,data,movement_clip)
            movement_time=max(float(t[-1]) for _,_,t,_ in movement_tracks)*.5
            for phase in (.15,.28,.58):
                layered={}
                for source,time in ((movement_tracks,movement_time),(reload_tracks,phase*duration)):
                    for node,path,times,frames in source:
                        layered[node,path]=(node,path,np.array([0.]),np.array([sample(times,frames,time,path=='rotation')]))
                actual=pose(model,list(layered.values()),0)
                expected=pose(model,reload_tracks,phase*duration)
                for node in model['skins'][0]['joints']:
                    np.testing.assert_allclose(actual[node],expected[node],atol=1e-5,
                        err_msg=f'{pistol}: {movement} leaked into reload joint {node}')
        directions=[]
        for time in np.linspace(0,duration,15):
            axis=pose(model,reload_tracks,time)[fx][:3,0]
            directions.append(axis/np.linalg.norm(axis))
        assert any(axis @ direction < .95 for axis in directions), f'{pistol}: muzzle did not follow reload'
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
        if pistol=='magnum44':
            # Adding cartridge joints must never replace the source arms' authored bind pose.
            base,base_data=read(SOURCE/'arms_base.glb')
            original=values(base,base_data,base['skins'][0]['inverseBindMatrices'])
            np.testing.assert_array_equal(values(model,data,skin['inverseBindMatrices'])[:len(original)],original)
            cases=[i for i,n in enumerate(model['nodes']) if n.get('name','').startswith('SpentCase') and 'mesh' not in n]
            fresh=[i for i,n in enumerate(model['nodes']) if n.get('name','').startswith('FreshRound') and 'mesh' not in n]
            assert len(cases)==len(fresh)==6
            reload=next(a for a in model['animations'] if a['name']=='FirstReloStFd2')
            compiled=tracks(model,data,reload);duration=max(float(t[-1]) for _,_,t,_ in compiled)
            # Opening must leave room for the glove around each hand joint.
            # The old upward reach brought opposing finger joints within 9 mm.
            hand_joints=[]
            for side in ('L','R'):
                prefixes=tuple(side+'_'+part for part in ('wrist','thumb','point','middle','ring','pink'))
                hand_joints.append([i for i,n in enumerate(model['nodes'])
                                    if n.get('name','').startswith(prefixes)])
            for phase in np.linspace(.10,.28,60):
                frame=pose(model,compiled,phase*duration)
                left_points=np.array([frame[i][:3,3] for i in hand_joints[0]])
                right_points=np.array([frame[i][:3,3] for i in hand_joints[1]])
                clearance=np.linalg.norm(left_points[:,None]-right_points[None,:],axis=2).min()
                assert clearance>.025, f'Opening hands overlap at phase {phase:.3f}'
            # Keep the support hand attached through the visible withdrawal,
            # including the old .68-.73 interval where it left the loader early.
            contact=pose(model,compiled,.58*duration)
            wrist=next(i for i,n in enumerate(model['nodes']) if n.get('name','').startswith('L_wrist_'))
            grip=np.linalg.inv(contact[66])@contact[wrist]
            loader_joint=skin['joints'].index(66)
            loader_vertices=[]
            for mesh in model['meshes']:
                for part in mesh['primitives']:
                    attrs=part['attributes']
                    own=values(model,data,attrs['JOINTS_0'])[:,0]==loader_joint
                    loader_vertices.extend(values(model,data,attrs['POSITION'])[own])
            loader_centre=(contact[66]@binds[loader_joint]@np.r_[np.mean(loader_vertices,axis=0),1])[:3]
            tips=[next(i for i,n in enumerate(model['nodes']) if n.get('name','').startswith(prefix))
                  for prefix in ('L_thumb3_', 'L_point3_')]
            pinch=np.mean([contact[i][:3,3] for i in tips],axis=0)
            assert np.linalg.norm(pinch-loader_centre)<.005, 'Fingers miss the speedloader grip'
            for phase in [.40,.46,.58,.66,.70,.72]:
                frame=pose(model,compiled,phase*duration)
                expected=frame[66]@grip
                assert np.linalg.norm(frame[wrist][:3,3]-expected[:3,3]) < .001
            before=pose(model,compiled,.22*duration);ejected=pose(model,compiled,.38*duration)
            assert np.linalg.norm(ejected[cases[0]][:3,3]-before[cases[0]][:3,3])>.04
            tipped=pose(model,compiled,.28*duration)
            bore=tipped[fx][:3,0];bore=bore/np.linalg.norm(bore)
            assert bore[1]>.25, 'Muzzle must tip up before cases leave the cylinder'
            # Before tumbling, acceleration must be vertical regardless of the
            # cylinder's tilt. Sample authored cartridge keys to avoid resampling.
            falling=[pose(model,compiled,k/120*duration)[cases[0]][:3,3] for k in (34,35,36)]
            assert falling[2][1]<falling[1][1]<falling[0][1]
            acceleration=(falling[2]-2*falling[1]+falling[0])/(duration/120)**2
            np.testing.assert_allclose(acceleration,[0,-9.81,0],atol=.15)
            scales=[(node,frames) for node,path,_,frames in compiled if path=='scale' and node in fresh]
            assert len(scales)==6 and all(frames.min()==0 and frames.max()==1 for _,frames in scales)
            # Cylinder opens to the shooter's left (-Z in the authored gun frame).
            opened=pose(model,compiled,.22*duration)
            camera=next(n for n in model['nodes'] if n.get('name')=='MercPistolFrame')
            placement=np.asarray(camera['matrix']).reshape(4,4).T@np.array([[0.,0,-1,0],[0,1,0,.089],[1,0,0,.153],[0,0,0,1]])
            follow=opened[57]@np.linalg.inv(world[57])
            movement=opened[80][:3,3]-(follow@world[80])[:3,3]
            right=follow[:3,:3]@placement[:3,2]
            assert movement@(right/np.linalg.norm(right)) < -.035
            # The textured source cartridges themselves are rigged. There is no fixed set
            # left in the cylinder underneath procedural duplicates.
            source,blob=read(SOURCE/'magnum44.glb')
            source_skin=source['skins'][0]
            for i in range(6):
                assert 'round'+str(i) in {source['nodes'][j]['name'] for j in source_skin['joints']}
                live=next(n for n in fresh if model['nodes'][n]['name']==f'FreshRound{i}')
                joint=skin['joints'].index(live)
                assert any(np.count_nonzero(values(model,data,p['attributes']['JOINTS_0'])[:,0]==joint)>300
                    for mesh in model['meshes'] for p in mesh['primitives'])
                case_mesh=next(n for n in model['nodes'] if n.get('name')==f'SpentCase{i}_mesh')
                for part in model['meshes'][case_mesh['mesh']]['primitives']:
                    assert 'TEXCOORD_0' in part['attributes']
                    assert 'Taurus' in model['materials'][part['material']]['name']
                approaching=pose(model,compiled,.46*duration)
                seated=pose(model,compiled,.58*duration)
                carried=approaching[66]@np.linalg.inv(seated[66])@seated[live]
                np.testing.assert_allclose(approaching[live][:3,3],carried[:3,3],atol=.001)
                assert np.linalg.norm(approaching[live][:3,3]-approaching[80][:3,3]) > .06
                assert np.linalg.norm(seated[live][:3,3]-seated[80][:3,3]) < 1e-5
                # Loader withdraws while the same fresh rounds remain seated in the chambers.
                withdrawn=pose(model,compiled,.72*duration)
                assert np.linalg.norm(withdrawn[live][:3,3]-withdrawn[80][:3,3]) < 1e-5
            assert all(reload['samplers'][c['sampler']]['interpolation']=='STEP' for c in reload['channels']
                if c['target']['node'] in fresh+cases and c['target']['path']=='scale')


def test_audio_exports_are_reproducible_and_cover_every_pistol(tmp_path):
    install_audio(tmp_path)
    paths=list((tmp_path/'wav/OpenScctSidearms').glob('*.wav'))
    assert len(paths)==2*len(IDS)
    digests={p.name:hashlib.sha256(p.read_bytes()).digest() for p in paths}
    install_audio(tmp_path)
    assert {p.name:hashlib.sha256(p.read_bytes()).digest() for p in paths}==digests
    for pistol in IDS:
        recording=tmp_path/'wav/OpenScctSidearms'/(pistol+'_fire.wav')
        assert recording.read_bytes()==(SOURCE/'audio'/(pistol+'_fire.wav')).read_bytes()
        with wave.open(str(recording),'rb') as wav:
            pcm=np.frombuffer(wav.readframes(wav.getnframes()),dtype='<i2').astype(float)
            audible=np.flatnonzero(np.abs(pcm)>np.abs(pcm).max()*.025)
            assert audible[0] <= 88, f'{pistol} has more than 2 ms of leading silence'
            assert np.abs(pcm).max() < 32767
    assert (tmp_path/'wav/OpenScctSidearms/CREDITS.txt').is_file()
    metadata=json.loads((tmp_path/'sidearms.json').read_text())['sounds']
    assert all(metadata['OpenScctSidearms.'+p+'_fire']['volume']==2.5118864 for p in IDS)
    timing=json.loads((SOURCE/'audio/timing.json').read_text())
    for pistol,events in timing.items():
        with wave.open(str(tmp_path/'wav/OpenScctSidearms'/(pistol+'_reload.wav')),'rb') as wav:
            pcm=np.frombuffer(wav.readframes(wav.getnframes()),dtype='<i2').astype(float)
            assert len(pcm)==round(events['seconds']*44100)
            for time in events['events']:
                first=round(time*44100)
                assert np.abs(pcm[first:first+4410]).max()>2000, f'{pistol}: missing mechanical cue at {time}'
