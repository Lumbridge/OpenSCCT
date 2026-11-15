"""Install the user-selected sound pack as the final weapon-sound overlay."""
from pathlib import Path
import json
import shutil
import wave
import array

SOURCE=Path(__file__).resolve().parents[1]/'assets/user_gun_audio/export'


def source_files():return sorted(p for p in SOURCE.rglob('*') if p.is_file())


def tactical_reload(source_path, target_path, definition):
    """Keep the magazine cues, omitting the M4 empty-magazine bolt release."""
    with wave.open(str(source_path), 'rb') as source:
        rate = source.getframerate()
        if source.getnchannels() != 1 or source.getsampwidth() != 2:
            raise ValueError('Reload recording must be mono PCM16')
        pcm = array.array('h', source.readframes(round(rate * 1.72)))
    fade = min(len(pcm), round(rate * .025))
    for i in range(fade): pcm[-fade+i] = round(pcm[-fade+i] * (1-i/(fade-1)))
    with wave.open(str(target_path), 'wb') as output:
        output.setparams((1, 2, rate, 0, 'NONE', 'not compressed'))
        output.writeframes(pcm.tobytes())
    return dict(definition, file=definition['file'].rsplit('/', 1)[0] + '/' + target_path.name,
                frames=len(pcm), duration=len(pcm)/rate)


def install(audio_dir):
    target=audio_dir/'wav/OpenScctUserWeapons';target.mkdir(parents=True,exist_ok=True)
    bank=json.loads((SOURCE/'manifest.json').read_text());sounds={}
    def recording(asset,file,volume=1):
        shutil.copy2(SOURCE/file,target/file)
        with wave.open(str(target/file),'rb') as f:
            if (f.getnchannels(),f.getsampwidth(),f.getframerate())!=(1,2,44100):raise ValueError('Gun SFX must be mono 44.1 kHz PCM16')
            frames=f.getnframes()
        sounds[asset]={'kind':'wave','flags':0,'file':'wav/OpenScctUserWeapons/'+file,'volume':volume,
            'radius':0,'loop':False,'positional':True,'frames':frames,'duration':frames/44100,'rate':44100,'bits':16,'channels':1}
    for name,profile in bank['profiles'].items():
        files=profile['shots'];weights=[10000//len(files)]*len(files);weights[0]+=10000-sum(weights);entries=[]
        for file,weight in zip(files,weights):
            asset='OpenScctUserWeapons.'+Path(file).stem;recording(asset,file);entries.append({'sound':asset,'value':weight})
        sounds[profile['asset']]={'kind':'random','flags':256,'entries':entries,'volume':2.5118864,'radius':0,'loop':False,'positional':True}
        if 'reload' in profile:recording(profile['reload']['asset'],profile['reload']['file'])
        if name == 'm4a1s':
            tactical = target / 'm4a1s_reload_tactical.wav'
            sounds['OpenScctRifles.m4a1s_reload_tactical'] = tactical_reload(
                SOURCE / profile['reload']['file'], tactical, sounds[profile['reload']['asset']])
    for asset,file in bank['extra'].items():recording(asset,file)
    # Retail weapon names retain their original recordings. This bank owns only
    # the custom rifles and sidearms, even when it is installed as the final overlay.
    for file in ('CREDITS.txt','manifest.json'):shutil.copy2(SOURCE/file,target/file)
    (audio_dir/'user-gun-audio.json').write_text(json.dumps({'format':'svm.audio/1','sounds':sounds},sort_keys=True),encoding='utf-8')
    return {'profiles':len(bank['profiles']),'entries':len(sounds)}
