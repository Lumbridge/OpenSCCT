"""Convert the user's Gun SFX collection into the current weapon bank.

First import: python author.py --source "C:/path/to/Gun SFX"
Subsequent builds use the retained selected source files. Requires numpy/ffmpeg.
"""
from pathlib import Path
import argparse
import hashlib
import json
import shutil
import subprocess
import wave
import numpy as np

ROOT=Path(__file__).resolve().parent
RATE=44100
# IDs retain existing gameplay assets. Rifle substitutes are calibre-matched
# references where the supplied folder has no exact M4/Galil recording.
PROFILES={
 'suppressed':('USP40','USP40_Fire_Single_Silenced_Mono_*.wav','USP40_Reload_Empty.wav',.42,.50,2.,[.315,1.6,2.675],[.14,.56,.66],'OpenScctSidearms'),
 'unsuppressed':('USP40','USP40_Fire_Single_Unsilenced_Mono_*.wav','USP40_Reload_Empty.wav',.64,.86,2.,[.315,1.6,2.675],[.14,.56,.66],'OpenScctSidearms'),
 'automatic':('Glock 18','Glock18_Fire_Single_Mono_*.wav','Glock18_Reload_Empty.wav',.42,.82,2.3,[.45,2.06,2.695],[.14,.56,.66],'OpenScctSidearms'),
 'magnum44':('Revolver','Revolver_Fire_Mono_*.wav','Revolver_Reload_Empty.wav',.80,.92,2.8,[.355,.965,1.59,2.925],[.12,.28,.58,.82],'OpenScctSidearms'),
 'desert_eagle':('Desert Eagle','Desert_Eagle_Fire_Mono_*.wav','Desert_Eagle_Reload_Empty.wav',.86,.90,2.6,[.355,2.06,2.71],[.14,.56,.66],'OpenScctSidearms'),
 'm4a1s':('552 Commando','552_Commando_Fire_Single_Silenced_Mono_*.wav','552_Commando_Reload_Empty.wav',.48,.48,2.4,[.48,1.66,2.65],[.11,.60,.80],'OpenScctRifles'),
 'ak47':('AK47','AK47_Fire_Single_Unsilenced_Mono_*.wav','AK47_Reload_Empty.wav',.65,.82,2.8,[.54,1.62,2.795],[.11,.60,.80],'OpenScctRifles'),
 'galil':('552 Commando','552_Commando_Fire_Single_Unsilenced_Mono_*.wav','552_Commando_Reload_Empty.wav',.58,.80,3.,[.48,1.66,2.65],[.11,.60,.80],'OpenScctRifles'),
 'famas':('FAMAS','FAMAS_Fire_Single_Unsilenced_Mono_*.wav','FAMAS_Reload_Empty.wav',.50,.80,2.65,[.54,1.445,2.285],[.11,.60,.80],'OpenScctRifles'),
 'rifle':('AUG A3','AUG_A3_Fire_Single_Unsilenced_Mono_*.wav','AUG_A3_Reload_Empty.wav',.65,.82,2.,[.535,1.415,2.5],[.14,.56,.66],'OpenScctWeapons'),
 'uzi':('Glock 18','Glock18_Fire_Single_Mono_*.wav','Glock18_Reload_Empty.wav',.42,.80,2.,[.45,2.06,2.695],[.14,.56,.66],'OpenScctWeapons'),
 'shotgun':('Benelli M3','Benelli_M3_Fire_Single_Mono_*.wav',None,.85,.88,None,[],[],'OpenScctWeapons'),
}


def read(path):
    raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-ac','1','-ar',str(RATE),'-f','f32le','pipe:1'])
    return np.frombuffer(raw,dtype='<f4').astype(float)


def fade(data):
    data=data.copy();n=min(len(data),round(.015*RATE))
    data[:8]*=np.linspace(0,1,min(8,len(data)))
    data[-n:]*=np.linspace(1,0,n)
    return data


def shot(data,seconds):
    data-=data.mean();hits=np.flatnonzero(abs(data)>max(1e-8,max(abs(data))*.08))
    if not len(hits):raise ValueError('Silent source')
    start=max(0,int(hits[0])-44);result=data[start:start+round(seconds*RATE)].copy()
    # Keep the supplied report's timbre; the game's room acoustics provide the
    # environment, so long baked tails fade before the end of each one-shot.
    time=np.arange(len(result))/RATE;result*=np.interp(time,[0,seconds*.5,seconds],[1,1,.12])
    return fade(result)


def save(path,data,peak):
    if not np.isfinite(data).all() or not np.any(data):raise ValueError('Invalid audio '+str(path))
    data=data*peak/max(abs(data))
    with wave.open(str(path),'wb') as f:
        f.setparams((1,2,RATE,len(data),'NONE','not compressed'));f.writeframes(np.rint(data*32767).astype('<i2').tobytes())


def main():
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source',type=Path);args=parser.parse_args()
    sources=ROOT/'sources';out=ROOT/'export';sources.mkdir(parents=True,exist_ok=True);out.mkdir(exist_ok=True)
    if args.source:
        selected=set()
        for folder,pattern,reload,*_ in PROFILES.values():
            selected.update((args.source/folder).glob(pattern))
            if reload:selected.add(args.source/folder/reload)
        selected.update([args.source/'Benelli M3/Benelli_M3_Reload_Cycle.wav',args.source/'Benelli M3/Benelli_M3_Reload.wav'])
        manifest={'source':'User-supplied Gun SFX collection','license':'not stated in supplied folder','files':[]}
        for path in sorted(selected):
            relative=path.relative_to(args.source);target=sources/relative;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(path,target)
            manifest['files'].append({'file':relative.as_posix(),'bytes':path.stat().st_size,'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
        (sources/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    bank={'profiles':{},'extra':{}}
    for name,(folder,pattern,reload,seconds,peak,duration,cues,fractions,package) in PROFILES.items():
        files=sorted((sources/folder).glob(pattern))
        if not files:raise FileNotFoundError(f'No recordings for {name}')
        profile={'asset':package+'.'+name+'_fire','shots':[]}
        for index,path in enumerate(files):
            file=f'{name}_fire_{index+1}.wav';save(out/file,shot(read(path),seconds),peak);profile['shots'].append(file)
        if reload:
            data=read(sources/folder/reload);result=np.zeros(round(duration*RATE));events=[]
            for cue,fraction in zip(cues,fractions):
                # The supplied reload is a composite. Align each main recorded
                # contact with the existing animation rather than changing its speed.
                begin=max(0,round((cue-.025)*RATE));clip=fade(data[begin:begin+round(.24*RATE)])
                at=max(0,round((duration*fraction-.025)*RATE));count=min(len(clip),len(result)-at);result[at:at+count]+=clip[:count]
                events.append(round(duration*fraction,6))
            file=name+'_reload.wav';save(out/file,result,.65);profile['reload']={'asset':package+'.'+name+'_reload','file':file,'seconds':duration,'events':events}
        bank['profiles'][name]=profile
    for name,path in [('shotgun_shell','Benelli M3/Benelli_M3_Reload.wav'),('shotgun_pump','Benelli M3/Benelli_M3_Reload_Cycle.wav')]:
        file=name+'.wav';save(out/file,shot(read(sources/path),.40),.62);bank['extra']['OpenScctWeapons.'+name]=file
    (out/'manifest.json').write_text(json.dumps(bank,indent=2)+'\n');shutil.copy2(ROOT/'CREDITS.txt',out/'CREDITS.txt')
    print('Built',sum(len(p['shots']) for p in bank['profiles'].values()),'single-shot variations from the supplied folder')


if __name__=='__main__':main()
