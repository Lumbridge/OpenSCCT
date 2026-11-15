"""Author realistic sidearm reports and handling from credited firearm recordings.

Python/numpy and ffmpeg/ffprobe are required. Run from any working directory.
The output uses existing SCCT timings; no synthetic gunshot or dynamics limiter.
"""
from pathlib import Path
import json
import shutil
import subprocess
import wave
import numpy as np

ROOT = Path(__file__).resolve().parent
OUT = ROOT/'export'
OLD = ROOT.parent/'sidearms/sources/audio'
RATE = 44100


def read(path):
    meta = json.loads(subprocess.check_output(['ffprobe','-v','error','-show_entries','stream=channels','-of','json',str(path)]))
    channels = meta['streams'][0]['channels']
    raw = subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-ar',str(RATE),'-f','f32le','pipe:1'])
    frames = np.frombuffer(raw,dtype='<f4').reshape(-1,channels).astype(float)
    # These recordings use separate microphone perspectives in L/R. Select a
    # full microphone signal rather than folding them together with phase loss.
    channel = int(np.mean(frames**2,axis=0).argmax())
    return frames[:,channel],channel


def excerpt(data, seconds, near=0, search=.35):
    """Keep two milliseconds ahead of the direct attack, including its first cycle."""
    start=max(0,round(near*RATE));end=min(len(data),start+round(search*RATE))
    probe=abs(data[start:end]);hits=np.flatnonzero(probe>max(1e-8,probe.max()*.12))
    if not len(hits):raise ValueError('No attack in the selected recording')
    first=max(0,start+int(hits[0])-round(.002*RATE))
    output=data[first:first+round(seconds*RATE)].copy()
    output-=np.mean(output)
    output[:8]*=np.linspace(0,1,min(8,len(output)))
    fade=min(len(output),round(.03*RATE));output[-fade:]*=np.linspace(1,0,fade)
    return output,first/RATE


def colour(data, low=1, high=1, air=1):
    size=1<<(len(data)*2-1).bit_length();freq=np.fft.rfftfreq(size,1/RATE)
    # Retain low-mid body; remove subsonic rumble without the old broad bass cut.
    highpass=freq**2/(freq**2+32**2)
    body=1+(low-1)/(1+(freq/320)**2)
    presence=1+(high-1)*freq**2/(freq**2+1400**2)
    top=1+(air-1)*freq**2/(freq**2+7500**2)
    return np.fft.irfft(np.fft.rfft(data,size)*highpass*body*presence*top,size)[:len(data)]


def pitch(data, ratio):
    return np.interp(np.arange(0,len(data),ratio),np.arange(len(data)),data)


def decay(data, start, end, floor=.15):
    time=np.arange(len(data))/RATE
    return data*np.interp(time,[0,start,end],[1,1,floor])


def add(result, data, at=0, gain=1):
    offset=round(at*RATE);count=min(len(data),len(result)-offset)
    if count>0:result[offset:offset+count]+=data[:count]*gain


def save(path,data,peak):
    if not np.isfinite(data).all() or np.max(abs(data))<=0:raise ValueError('Invalid recording '+str(path))
    data=data*(peak/max(1e-8,np.max(abs(data))))
    with wave.open(str(path),'wb') as stream:
        stream.setparams((1,2,RATE,len(data),'NONE','not compressed'))
        stream.writeframes(np.rint(data*32767).astype('<i2').tobytes())


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    sources={};channels={}
    for path in sorted((ROOT/'sources').glob('*.wav')):sources[path.stem],channels[path.stem]=read(path)
    for name in ('magnum44_field','desert_eagle'):
        path=OLD/(name+('.wav' if name=='magnum44_field' else '.mp3'))
        sources[name],channels[name]=read(path)
    # Independent recorded actions, placed at the existing authored contact times.
    actions={key:excerpt(sources[file],duration,search=20)[0] for key,file,duration in
        [('release45','A_5',.16),('seat45','A_4',.24),('slide45','A_9',.20),
         ('release9','X_3',.14),('seat9','X_4',.19),('slide9','X_9',.18),
         ('open','V_0',.18),('close','V_1',.22),('load','V_2',.28),('eject','V_3',.24)]}
    profiles={
        'suppressed':{'source':'J_1','attacks':[.509,2.971,5.395],'seconds':.42,'peak':.50,'tone':(1.15,.88,.85)},
        'unsuppressed':{'source':'A_42P','attacks':[.938,4.998,.938],'seconds':.64,'peak':.86,'tone':(1.30,1.04,.92)},
        'automatic':{'source':'X_39P','attacks':[1.405,6.443,10.658],'seconds':.42,'peak':.82,'tone':(1.12,1.10,.90)},
        'magnum44':{'source':'magnum44_field','attacks':[0,0,0],'seconds':.80,'peak':.92,'tone':(1.35,.94,.84)},
        'desert_eagle':{'source':'desert_eagle','attacks':[1.062,1.062,1.062],'seconds':.86,'peak':.90,'tone':(1.28,1.02,.88)},
    }
    original45,_=excerpt(sources['A_42P'],.22,near=.938)
    manifest={'format':'openscct.sidearm-audio/1','channels':channels,'profiles':{}}
    for name,profile in profiles.items():
        files=[];starts=[]
        for i,near in enumerate(profile['attacks']):
            report,start=excerpt(sources[profile['source']],profile['seconds'],near=near)
            ratio=1 if name in ('automatic','suppressed') or name=='unsuppressed' and i<2 else (1,.992,1.009)[i]
            report=pitch(report,ratio)
            report=colour(report,*profile['tone'])
            report=decay(report,.12 if name=='automatic' else .18,profile['seconds'],.12 if name in ('desert_eagle','magnum44') else .20)
            result=np.zeros(round(profile['seconds']*RATE));add(result,report)
            if name=='suppressed':add(result,colour(original45,1.2,.10,.1),.001,.07)
            if name!='magnum44':
                action=actions['slide9' if name=='automatic' else 'slide45']
                add(result,colour(action,1,.85,.8),.026 if name=='automatic' else .038,.12 if name=='suppressed' else .055)
            file=f'{name}_fire_{i+1}.wav';save(OUT/file,result,profile['peak']);files.append(file);starts.append(round(start,6))
        manifest['profiles'][name]={'shots':files,'source':profile['source'],'source_attack_seconds':starts,'peak':profile['peak']}
    durations={'suppressed':2.,'unsuppressed':2.,'automatic':2.3,'magnum44':2.8,'desert_eagle':2.6}
    for name,seconds in durations.items():
        result=np.zeros(round(seconds*RATE))
        if name=='magnum44':cues=[(.12,'open'),(.28,'eject'),(.58,'load'),(.82,'close')]
        else:
            suffix='9' if name=='automatic' else '45'
            cues=[(.14,'release'+suffix),(.56,'seat'+suffix),(.66,'slide'+suffix)]
        for fraction,action in cues:
            clip=colour(actions[action],1.15 if name in ('desert_eagle','magnum44') else 1,1,.85)
            add(result,clip,seconds*fraction,.8 if action.startswith('release') else 1)
        save(OUT/(name+'_reload.wav'),result,.65 if name!='desert_eagle' else .70)
        manifest['profiles'][name]['reload']={'file':name+'_reload.wav','seconds':seconds,
            'events':[{'seconds':round(f*seconds,6),'action':action} for f,action in cues]}
    (OUT/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    shutil.copy2(ROOT/'CREDITS.txt',OUT/'CREDITS.txt')
    print('Built 15 shot variations and 5 synchronized reloads')


if __name__=='__main__':main()
