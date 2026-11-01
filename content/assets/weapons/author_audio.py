"""Build the redesigned weapon sound bank from credited recordings. Needs ffmpeg/numpy."""
from pathlib import Path
import json
import subprocess
import wave
import shutil
import numpy as np

ROOT = Path(__file__).resolve().parent
SIDE = ROOT.parent/'sidearms'
OUT = ROOT/'export'
RATE = 44100


def read(path):
    raw = subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-f','f32le','-ac','1','-ar',str(RATE),'pipe:1'])
    return np.frombuffer(raw,dtype='<f4').astype(float)


def cut(data, seconds, peak=.8):
    data = data-data.mean()
    hit = np.flatnonzero(np.abs(data) > max(np.abs(data).max()*.12,1e-7))
    if not len(hit):raise ValueError('Silent source recording')
    start = max(0,int(hit[0])-44)
    data = data[start:start+round(seconds*RATE)].copy()
    data[:22] *= np.linspace(0,1,min(22,len(data)))
    fade=min(round(.025*RATE),len(data));data[-fade:]*=np.linspace(1,0,fade)
    return data*peak/max(np.abs(data).max(),1e-8)


def tone(data, low, high):
    # Smooth shelving balance, no hard band cutoff or time-domain clipping.
    size=1 << (len(data)*2-1).bit_length()
    f=np.fft.rfftfreq(size,1/RATE)
    gain=(low+(high-low)*f/(f+1500))*(f/(f+35))
    return np.fft.irfft(np.fft.rfft(data,size)*gain,size)[:len(data)]


def add(dest, source, at, gain=1):
    offset=round(at*RATE);count=min(len(source),len(dest)-offset)
    if count>0:dest[offset:offset+count]+=source[:count]*gain


def save(name,data,peak):
    if not np.isfinite(data).all():raise ValueError('Invalid audio '+name)
    data*=peak/max(np.abs(data).max(),1e-8)
    with wave.open(str(OUT/(name+'.wav')),'wb') as f:
        f.setparams((1,2,RATE,len(data),'NONE','not compressed'))
        f.writeframes(np.rint(data*32767).astype('<i2').tobytes())


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    settings=json.loads((SIDE/'sources/audio/sources.json').read_text())
    sources={k:read(SIDE/'sources/audio'/v['file']) for k,v in settings.items()}
    rifle=read(ROOT/'sources/rifle.wav');carbine=read(ROOT/'sources/carbine.wav')
    pump=cut(read(ROOT/'sources/pump.wav'),.32,.7)
    handling=read(ROOT/'sources/rifle_reload.wav')
    magazine=sources['pistol_reload'];revolver=sources['revolver_reload']
    seat=cut(magazine[round(.55*RATE):round(.87*RATE)],.25,.65)
    release=cut(magazine[:round(.26*RATE)],.22,.5)
    bolt=cut(magazine[round(1.14*RATE):round(1.48*RATE)],.28,.6)
    shell=cut(read(ROOT/'sources/shell.mp3'),.24,.55)
    fire_sources={**{k:sources[k] for k in ('suppressed','automatic','unsuppressed','magnum44','desert_eagle')},
                  'rifle':rifle,'uzi':carbine,'shotgun':read(ROOT/'sources/shotgun.wav')}
    fire_styles={'rifle':(.65,1.15,1.1,.82),'uzi':(.30,.75,1.4,.78),'shotgun':(.70,1.4,.85,.85),
                 'suppressed':(.38,.8,1.2,.48),'automatic':(.30,.8,1.3,.74),
                 'unsuppressed':(.42,1.05,1.2,.8),'magnum44':(.65,1.35,1.05,.85),'desert_eagle':(.70,1.4,.95,.85)}
    for name,source in fire_sources.items():
        seconds,low,high,peak=fire_styles[name]
        result=np.zeros(round(seconds*RATE))
        source_seconds=min(seconds,settings.get(name,{}).get('max_seconds',seconds))
        add(result,tone(cut(source,source_seconds),low,high),0)
        # Separate action/clack provides a close handling layer without a long
        # canned room echo; spatial/reverb treatment remains the game's job.
        add(result,bolt[:round(.10*RATE)],.035,.13 if name!='suppressed' else .30)
        if name in ('rifle','uzi','automatic'):
            add(result,cut(carbine,.10,.4),.008,.20)
        save(name+'_fire',result,peak)
    timings={}
    seconds_by_name={'rifle':2.,'uzi':2.,'suppressed':2.,'unsuppressed':2.,'automatic':2.3,'magnum44':2.8,'desert_eagle':2.6}
    for name,seconds in seconds_by_name.items():
        if name=='magnum44':
            cues=[(.12,cut(revolver[round(a*RATE):round(b*RATE)],b-a,.65)) for a,b in [(1.30,1.56)]]
            cues += [(fraction,cut(revolver[round(a*RATE):round(b*RATE)],b-a,.65))
                     for fraction,a,b in [(.28,6.04,6.32),(.58,7.74,7.99),(.82,19.15,19.43)]]
        else:
            cues=[(.14,release),(.56,seat),(.66,bolt)]
        result=np.zeros(round(seconds*RATE))
        # Quiet grip/cloth/mechanism pickup, distinct from the hard latch cues.
        add(result,tone(cut(handling,.14,.2),.7,.5),.06,.45)
        for fraction,cue in cues:
            add(result,tone(cue,1.2 if name in ('rifle','desert_eagle','magnum44') else .9,1.0),fraction*seconds)
        save(name+'_reload',result,.64)
        timings[name]={'seconds':seconds,'events':[round(f*seconds,6) for f,_ in cues]}
    save('shotgun_reload',cut(handling,.32,.5),.5)
    save('shotgun_shell',shell,.55)
    save('shotgun_pump',pump,.65)
    timings['shotgun']={'seconds':3,'events':[.7880001,1.11,1.4413334,1.9193333,2.2793334,2.6],
                        'playback':'individual shell events; early exits stop future shell sounds'}
    (OUT/'timing.json').write_text(json.dumps(timings,indent=2)+'\n')
    shutil.copy2(ROOT/'CREDITS.txt',OUT/'CREDITS.txt')
    shutil.copy2(SIDE/'CREDITS.txt',OUT/'SIDEARM-CREDITS.txt')
    print('Built',len(list(OUT.glob('*.wav'))),'weapon recordings')


if __name__=='__main__':main()
