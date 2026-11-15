"""Build mono rifle reports and reload handling from the CC0 sources in CREDITS.txt."""
from pathlib import Path
import json
import subprocess
import wave
import numpy as np

ROOT=Path(__file__).resolve().parent
OUT=ROOT/'export/audio'
RATE=44100


def read(path):
    raw=subprocess.check_output(['ffmpeg','-v','error','-i',str(path),'-ac','1','-ar',str(RATE),'-f','f32le','pipe:1'])
    return np.frombuffer(raw,dtype='<f4').astype(float)


def cut(data,seconds,peak=.8):
    data=data-data.mean();hits=np.flatnonzero(np.abs(data)>max(1e-8,np.abs(data).max()*.15))
    if not len(hits):raise ValueError('Silent recording')
    start=max(0,int(hits[0])-44);data=data[start:start+round(seconds*RATE)].copy()
    fade=min(len(data),round(.025*RATE));data[-fade:]*=np.linspace(1,0,fade);data[:22]*=np.linspace(0,1,min(22,len(data)))
    return data*peak/max(1e-8,np.abs(data).max())


def tone(data,low=1,high=1):
    count=1<<(len(data)*2-1).bit_length();f=np.fft.rfftfreq(count,1/RATE)
    gain=(low+(high-low)*f/(f+1600))*(f/(f+40))
    return np.fft.irfft(np.fft.rfft(data,count)*gain,count)[:len(data)]


def add(result,data,time,gain=1):
    start=round(time*RATE);length=min(len(data),len(result)-start)
    if length>0:result[start:start+length]+=data[:length]*gain


def save(name,data,peak):
    data*=peak/max(1e-8,np.abs(data).max())
    with wave.open(str(OUT/(name+'.wav')),'wb') as stream:
        stream.setparams((1,2,RATE,len(data),'NONE','not compressed'));stream.writeframes(np.rint(data*32767).astype('<i2').tobytes())


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    source=ROOT/'sources/audio';side=ROOT.parent/'sidearms/sources/audio';weapons=ROOT.parent/'weapons/sources'
    ak=cut(read(source/'C_28P.wav'),.65);ar=cut(read(source/'D_32P.wav'),.58);distant=cut(read(source/'D_24P.wav'),.6)
    quiet=cut(read(side/'suppressed.mp3'),.32,.5);handling=read(weapons/'rifle_reload.wav');pistol=read(side/'pistol_reload.mp3')
    release=cut(pistol[:round(.26*RATE)],.22,.5);seat=cut(pistol[round(.55*RATE):round(.87*RATE)],.25,.6)
    bolt=cut(pistol[round(1.14*RATE):round(1.48*RATE)],.28,.6)
    settings={'m4a1s':(2.4,.46,.9,.6,.42),'ak47':(2.8,.65,1.15,.9,.82),
              'galil':(3.,.58,1.05,1.15,.78),'famas':(2.65,.50,.9,1.3,.76)}
    timing={}
    for name,(reload,seconds,low,high,peak) in settings.items():
        result=np.zeros(round(seconds*RATE));report=ak if name=='ak47' else ar
        if name=='m4a1s':
            # Actual suppressed report + a restrained rifle crack and action layer.
            add(result,tone(quiet,1.2,.7),0)
            add(result,tone(ar,.5,.16),0,.12)
        else:
            rate={'ak47':1.,'galil':.97,'famas':1.04}[name]
            report=np.interp(np.arange(0,len(report),rate),np.arange(len(report)),report)
            add(result,tone(report,low,high),0)
            if name=='galil':add(result,tone(distant,.9,.75),.008,.16)
        add(result,bolt[:round(.09*RATE)],.035,.16 if name!='m4a1s' else .24);save(name+'_fire',result,peak)
        result=np.zeros(round(reload*RATE));add(result,tone(cut(handling,.16,.2),.7,.5),.05,.5)
        cues=[(.11,release),(.60,seat),(.80,bolt)]
        for fraction,clip in cues:add(result,tone(clip,1.15 if name=='ak47' else 1,1),fraction*reload)
        save(name+'_reload',result,.60);timing[name]={'seconds':reload,'events':{'release':round(.11*reload,4),'seat':round(.60*reload,4),'bolt':round(.80*reload,4)}}
    (OUT/'timing.json').write_text(json.dumps(timing,indent=2)+'\n')
    print('Built 8 rifle recordings')


if __name__=='__main__':main()
