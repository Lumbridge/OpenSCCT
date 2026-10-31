"""Install OpenSCCT's authored sidearms and deterministically build their finishes and audio."""
from pathlib import Path
import json
import math
import random
import shutil
import struct
import wave

SOURCE = Path(__file__).resolve().parents[1] / 'assets' / 'sidearms' / 'export'
IDS = ('suppressed', 'automatic', 'magnum44', 'desert_eagle')
FINISHES = ('night_ops', 'cryo', 'woodland', 'desert', 'digital', 'red_tiger', 'blue_tiger', 'golden')


def source_files():
    return sorted(p for p in SOURCE.rglob('*') if p.is_file())


def install_models(chars_dir: Path):
    from .cosmetics import _paint_job
    target = chars_dir / 'OpenScctSidearms'
    shutil.copytree(SOURCE, target, dirs_exist_ok=True)
    from .sidearm_models import assemble
    retail=chars_dir/"SWeaponFirst"
    for image in (retail/"textures").glob("*.png"):
        if image.name.startswith("cosmetic_") and "1DEF_01" not in image.name:continue
        shutil.copy2(image,target/"textures"/image.name)
    for pistol in IDS:
        assemble(chars_dir,pistol)
        from .sidearm_models import read
        model, _ = read(target / (pistol + '.glb'))
        textures = set()
        for material in model['materials']:
            texture = material.get('pbrMetallicRoughness', {}).get('baseColorTexture')
            if texture is not None:
                image = model['textures'][texture['index']]['source']
                textures.add(model['images'][image]['uri'])
        for uri in sorted(textures):
            image = target / uri
            for finish in FINISHES:
                _paint_job((str(image), str(image.with_name(f'cosmetic_{finish}_{image.name}')),
                            finish, image.stem, (1024, 1024), None))
    arm_finishes = install_arm_finishes(chars_dir)
    return {'models': 8, 'finishes': 32, 'icons': 4, 'arm_finishes': arm_finishes}


def install_arm_finishes(chars_dir: Path):
    """The new 2K sleeves retain the merc's existing independently selected body finishes."""
    from concurrent.futures import ThreadPoolExecutor
    from .cosmetics import _MERC_SUITS, _paint_job
    source = chars_dir / 'OpenScctSidearms/textures/OpenScctSidearms_arms_arms_baseColor.png'
    jobs = [(str(source), str(source.with_name(f'cosmetic_{pattern}_{source.name}')), pattern,
             source.stem, (2048, 2048), None) for pattern in sorted(set(_MERC_SUITS))]
    with ThreadPoolExecutor(max_workers=2) as pool:
        return len(list(pool.map(_paint_job, jobs)))


def install_audio(audio_dir: Path):
    """Original synthesized recordings: sharp impulse, body, decay and mechanical reload transients."""
    folder = audio_dir / 'wav' / 'OpenScctSidearms'
    folder.mkdir(parents=True, exist_ok=True)
    sounds = {}
    rate = 44100
    for index, pistol in enumerate(IDS):
        for action in ('fire', 'reload'):
            rng = random.Random(pistol + action)
            seconds = (.32, .28, .7, .62)[index] if action == 'fire' else (2, 2.3, 2.8, 2.6)[index]
            count = int(rate * seconds)
            data = bytearray()
            low = 0.0
            for frame in range(count):
                t = frame / rate
                noise = rng.uniform(-1, 1)
                low = low * .82 + noise * .18
                if action == 'fire':
                    decay = math.exp(-t * (30 if index == 0 else 13))
                    body = math.sin(math.tau * (120 + index * 15) * t) * math.exp(-t * 22)
                    value = ((low if index == 0 else noise*.65+low*.35)*decay + body*.35) * .8
                else:
                    value = 0
                    for event in (.08, seconds*.27, seconds*.63, seconds*.86):
                        if t >= event:
                            age=t-event
                            value += (noise*.45+math.sin(math.tau*1700*age)*.1)*math.exp(-age*100)
                data.extend(struct.pack('<h', int(max(-1, min(1, value))*30000)))
            name = pistol + '_' + action
            with wave.open(str(folder/(name+'.wav')), 'wb') as output:
                output.setparams((1, 2, rate, count, 'NONE', 'not compressed'))
                output.writeframes(data)
            sounds['OpenScctSidearms.'+name] = {
                'kind':'wave','flags':0,'file':'wav/OpenScctSidearms/'+name+'.wav',
                'loop':False,'positional':True,'radius':0,'volume':1,
                'duration':seconds,'frames':count,'rate':rate,'bits':16,'channels':1,
            }
    (audio_dir/'sidearms.json').write_text(json.dumps({'format':'svm.audio/1','sounds':sounds},sort_keys=True),encoding='utf-8')
    return {'sounds':len(sounds)}
