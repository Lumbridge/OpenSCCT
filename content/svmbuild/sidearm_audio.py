"""Install the current sidearm sound bank after the older weapon overlay."""
from pathlib import Path
import json
import shutil
import wave

SOURCE=Path(__file__).resolve().parents[1]/'assets/sidearm_audio/export'


def source_files():
    return sorted(p for p in SOURCE.rglob('*') if p.is_file())


def install(audio_dir):
    manifest=json.loads((SOURCE/'manifest.json').read_text())
    target=audio_dir/'wav/OpenScctSidearmAudio';target.mkdir(parents=True,exist_ok=True)
    sounds={}
    def recording(file,asset,volume=1):
        destination=target/file;shutil.copy2(SOURCE/file,destination)
        with wave.open(str(destination),'rb') as stream:
            if (stream.getnchannels(),stream.getsampwidth(),stream.getframerate())!=(1,2,44100):raise ValueError('Sidearm audio must be mono 44.1 kHz PCM16')
            frames=stream.getnframes()
        sounds[asset]={'kind':'wave','flags':0,'file':'wav/OpenScctSidearmAudio/'+file,
            'volume':volume,'radius':0,'positional':True,'loop':False,
            'duration':frames/44100,'frames':frames,'rate':44100,'bits':16,'channels':1}
    for name,profile in manifest['profiles'].items():
        entries=[]
        for index,file in enumerate(profile['shots']):
            asset='OpenScctSidearmAudio.'+Path(file).stem;recording(file,asset)
            entries.append({'sound':asset,'value':3334 if index==0 else 3333})
        sounds['OpenScctSidearms.'+name+'_fire']={'kind':'random','flags':256,'entries':entries,
            'volume':2.5118864,'radius':0,'positional':True,'loop':False}
        recording(profile['reload']['file'],'OpenScctSidearms.'+name+'_reload')
    for file in ('CREDITS.txt','manifest.json'):shutil.copy2(SOURCE/file,target/file)
    (audio_dir/'sidearm-audio.json').write_text(json.dumps({'format':'svm.audio/1','sounds':sounds},sort_keys=True),encoding='utf-8')
    return {'reports':15,'reloads':5,'entries':len(sounds)}
