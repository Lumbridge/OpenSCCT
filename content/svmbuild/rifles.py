"""Install credited rifle art/audio and locally retarget SCCT contextual motions."""
from pathlib import Path
import json
import shutil
import wave

SOURCE = Path(__file__).resolve().parents[1] / 'assets' / 'rifles' / 'export'
IDS = ('m4a1s', 'ak47', 'galil', 'famas')


def source_files():
    return sorted(p for p in SOURCE.rglob('*') if p.is_file())


def contextual_clips(path, reference_path):
    from .sidearm_models import read, write
    from .viewmodel_actions import author
    doc,data=read(path);reference,blob=read(reference_path)
    author(doc,data,reference,blob)
    write(path,doc,data)


def install_models(chars_dir):
    target = chars_dir / 'OpenScctRifles'
    shutil.copytree(SOURCE,target,dirs_exist_ok=True,ignore=shutil.ignore_patterns('audio'))
    # Body finishes use the same cloth atlas as the pistols, with a local URI.
    for image in (chars_dir/'OpenScctSidearms/textures').glob('cosmetic_*OpenScctSidearms_arms_arms_baseColor.png'):
        prefix,base=image.name.split('OpenScctSidearms_',1)
        shutil.copy2(image,target/'textures'/(prefix+'shared_OpenScctSidearms_'+base))
    from .rifle_handling import author_rifle, world_sights, split_m4_backup_sight
    from .weapon_icons import render
    for name in IDS:
        contextual_clips(target/(name+'_first.glb'),chars_dir/'SWeaponFirst/1DEF_01.glb')
        author_rifle(target/(name+'_first.glb'), name)
        if name in ('m4a1s', 'famas'):
            world_sights(target/(name+'.glb'), target/(name+'_first.glb'), name)
            render(target/(name+'.glb'), target/'textures'/(name+'_icon.png'))
    split_m4_backup_sight(SOURCE/'m4a1s.glb', target)
    weapons = json.loads((target / 'models.json').read_text(encoding='utf-8'))
    from .rifle_finishes import install as install_finishes
    return {
        'models': sum((target / (name + suffix + '.glb')).is_file() for name in weapons for suffix in ('', '_first')),
        'icons': sum((target / 'textures' / (name + '_icon.png')).is_file() for name in weapons),
        'finishes': install_finishes(target, SOURCE),
    }


def install_audio(audio_dir):
    target = audio_dir/'wav/OpenScctRifles';target.mkdir(parents=True,exist_ok=True)
    shutil.copy2(SOURCE/'CREDITS.txt',target/'CREDITS.txt')
    sounds = {}
    for name in IDS:
        for action in ('fire','reload'):
            key = name+'_'+action;file = target/(key+'.wav');shutil.copy2(SOURCE/'audio'/(key+'.wav'),file)
            with wave.open(str(file),'rb') as stream:
                frames = stream.getnframes();rate = stream.getframerate()
                if rate != 44100 or stream.getnchannels() != 1 or stream.getsampwidth() != 2:raise ValueError('Rifle audio must be mono 44.1 kHz PCM16')
            sounds['OpenScctRifles.'+key] = {'kind':'wave','flags':0,'file':'wav/OpenScctRifles/'+key+'.wav',
                'loop':False,'positional':True,'radius':0,'volume':2.5118864 if action=='fire' else 1,
                'duration':frames/rate,'frames':frames,'rate':rate,'bits':16,'channels':1}
    from .user_gun_audio import tactical_reload
    sounds['OpenScctRifles.m4a1s_reload_tactical'] = tactical_reload(
        target/'m4a1s_reload.wav', target/'m4a1s_reload_tactical.wav', sounds['OpenScctRifles.m4a1s_reload'])
    (audio_dir/'rifles.json').write_text(json.dumps({'format':'svm.audio/1','sounds':sounds},sort_keys=True),encoding='utf-8')
    return {'sounds':len(sounds)}
