"""Install credited weapon audio as a shared-library overlay."""
from pathlib import Path
import json
import shutil
import wave

SOURCE = Path(__file__).resolve().parents[1]/'assets/weapons/export'
SIDEARMS = ('suppressed','unsuppressed','automatic','magnum44','desert_eagle')


def install(audio_dir):
    target=audio_dir/'wav/OpenScctWeapons'
    target.mkdir(parents=True,exist_ok=True)
    sounds={}
    for path in sorted(SOURCE.glob('*.wav')):
        shutil.copy2(path,target/path.name)
        with wave.open(str(path),'rb') as f:
            if (f.getnchannels(),f.getsampwidth(),f.getframerate()) != (1,2,44100):
                raise ValueError('Weapon sound must be mono 16-bit 44.1 kHz: '+str(path))
            frames=f.getnframes()
        name=path.stem
        asset='OpenScctWeapons.'+name
        sounds[asset]={'kind':'wave','flags':0,'file':'wav/OpenScctWeapons/'+path.name,
            'loop':False,'positional':True,'radius':0,'volume':2.5118864 if name.endswith('_fire') else 1,
            'duration':frames/44100,'frames':frames,'rate':44100,'bits':16,'channels':1}
        if any(name.startswith(p+'_') for p in SIDEARMS):sounds['OpenScctSidearms.'+name]=sounds[asset]
    # Original weapon paths must retain their extracted recordings and sound lists.
    for file in ('CREDITS.txt','SIDEARM-CREDITS.txt','timing.json'):shutil.copy2(SOURCE/file,target/file)
    (audio_dir/'weapons.json').write_text(json.dumps({'format':'svm.audio/1','sounds':sounds},sort_keys=True),encoding='utf-8')
    return {'recordings':len(list(SOURCE.glob('*.wav'))),'entries':len(sounds)}
