// Isolated capability/capture probe for the owner's original DirectSound/EAX implementation.
// Loads an explicit vendor DLL, changes no device settings, and opens only this process's
// output-loopback capture when a WAV path is supplied. Never opens a microphone.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <mmsystem.h>
#include <dsound.h>
#include <ks.h>
#include <ksproxy.h>
#include <tlhelp32.h>
#include <cstdio>
#include <cstdint>
#include <string>
#include <cwctype>
#include <vector>
#include <cmath>
#include <algorithm>
#include "ProcessLoopbackCapture.h"

static const GUID Eax3Listener = {0xa8fa6882,0xb476,0x11d3,{0xbd,0xb9,0x00,0xc0,0xf0,0x2d,0xdf,0x87}};
static const GUID Eax3Source = {0xa8fa6881,0xb476,0x11d3,{0xbd,0xb9,0x00,0xc0,0xf0,0x2d,0xdf,0x87}};
static const GUID Eax2Listener = {0x0306a6a8,0xb224,0x11d2,{0x99,0xe5,0x00,0x00,0xe8,0xd8,0xc7,0x22}};
static const GUID KsPropertySet = {0x31efac30,0x515c,0x11d0,{0xa9,0xaa,0x00,0xaa,0x00,0x61,0xbe,0x93}};
static void JsonString(const wchar_t* text) {
    putchar('"');
    for (; *text; ++text) {
        if (*text=='"' || *text=='\\') putchar('\\');
        if (*text>=32 && *text<127) putchar(static_cast<char>(*text));
        else printf("\\u%04x",static_cast<unsigned>(*text));
    }
    putchar('"');
}
static void Hr(const char* key,HRESULT hr) { printf("\"%s\":\"0x%08lx\",",key,static_cast<unsigned long>(hr)); }

int wmain(int argc,wchar_t** argv) {
    if (argc<2 || argc>12) { fprintf(stderr,"Usage: Probe-OriginalEax.exe <vendor.dll> [listener.bin [capture.wav [seconds [4|16] [--distance=uu] [--pattern=impulse|double|late|tone] [--transition=listener.bin] [--transition-ms=1500]]]]\n"); return 2; }
    double distance=-1;bool doubleImpulse=false,lateImpulse=false,tone=false;int secondFrame=108000;const wchar_t* transition=nullptr;DWORD transitionMs=1500;
    for(int i=5;i<argc;++i) {
        if(wcsncmp(argv[i],L"--distance=",11)==0)distance=_wtof(argv[i]+11);
        if(wcscmp(argv[i],L"--pattern=double")==0)doubleImpulse=true;
        if(wcscmp(argv[i],L"--pattern=late")==0)lateImpulse=true;
        if(wcsncmp(argv[i],L"--second-frame=",15)==0)secondFrame=std::clamp(_wtoi(argv[i]+15),1,431999);
        if(wcscmp(argv[i],L"--pattern=tone")==0)tone=true;
        if(wcsncmp(argv[i],L"--transition=",13)==0)transition=argv[i]+13;
        if(wcsncmp(argv[i],L"--transition-ms=",16)==0)transitionMs=static_cast<DWORD>(std::clamp(_wtoi(argv[i]+16),0,29000));
    }
    const HRESULT com=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
    HMODULE module=LoadLibraryExW(argv[1],nullptr,LOAD_WITH_ALTERED_SEARCH_PATH);
    printf("{\"processId\":%lu,\"pointerBytes\":%zu,\"requestedDll\":",GetCurrentProcessId(),sizeof(void*));JsonString(argv[1]);printf(",");
    if (!module) { printf("\"loadError\":%lu}\n",GetLastError());return 3; }
    auto create=reinterpret_cast<HRESULT (WINAPI*)(LPCGUID,LPDIRECTSOUND8*,LPUNKNOWN)>(GetProcAddress(module,"DirectSoundCreate8"));
    LPDIRECTSOUND8 ds=nullptr;
    HRESULT hr=create?create(nullptr,&ds,nullptr):E_NOINTERFACE;Hr("DirectSoundCreate8",hr);
    IKsPropertySet* properties=nullptr; LPDIRECTSOUNDBUFFER buffer=nullptr;
    bool supported=false,played=false,blockApplied=argc<3;
    const bool driverDefault=argc>=3 && wcscmp(argv[2],L"--driver-default")==0;
    const bool sequentialOnly=argc==4 && wcsncmp(argv[3],L"--then=",7)==0;
    if (SUCCEEDED(hr) && ds) {
        Hr("SetCooperativeLevel",ds->SetCooperativeLevel(GetDesktopWindow(),DSSCL_NORMAL));
        WAVEFORMATEX format={};format.wFormatTag=WAVE_FORMAT_PCM;format.nChannels=1;format.nSamplesPerSec=48000;
        format.wBitsPerSample=16;format.nBlockAlign=2;format.nAvgBytesPerSec=96000;
        DSBUFFERDESC desc={};desc.dwSize=sizeof(desc);desc.dwFlags=DSBCAPS_CTRL3D|DSBCAPS_GLOBALFOCUS|DSBCAPS_GETCURRENTPOSITION2;
        desc.dwBufferBytes=96000*9;desc.lpwfxFormat=&format;desc.guid3DAlgorithm=DS3DALG_DEFAULT;
        hr=ds->CreateSoundBuffer(&desc,&buffer,nullptr);Hr("Create3DBuffer",hr);
        if (SUCCEEDED(hr) && buffer) {
            if(distance>=0) {
                LPDIRECTSOUNDBUFFER primary=nullptr;IDirectSound3DListener* listener=nullptr;IDirectSound3DBuffer* source3d=nullptr;
                DSBUFFERDESC primaryDesc={};primaryDesc.dwSize=sizeof(primaryDesc);primaryDesc.dwFlags=DSBCAPS_PRIMARYBUFFER|DSBCAPS_CTRL3D;
                hr=ds->CreateSoundBuffer(&primaryDesc,&primary,nullptr);Hr("CreatePrimaryListener",hr);
                if(SUCCEEDED(hr)) {
                    WAVEFORMATEX actual={};DWORD bytes=0;primary->GetFormat(&actual,sizeof(actual),&bytes);
                    printf("\"primaryRate\":%lu,\"primaryChannels\":%u,\"primaryBits\":%u,",actual.nSamplesPerSec,actual.nChannels,actual.wBitsPerSample);
                    hr=primary->QueryInterface(IID_IDirectSound3DListener,reinterpret_cast<void**>(&listener));Hr("QueryListener",hr);
                    if(SUCCEEDED(hr)) {
                        Hr("SetDistanceFactor",listener->SetDistanceFactor(.0127f,DS3D_IMMEDIATE));
                        Hr("SetRolloffFactor",listener->SetRolloffFactor(.5f,DS3D_IMMEDIATE));
                        Hr("SetDopplerFactor",listener->SetDopplerFactor(.5f,DS3D_IMMEDIATE));
                        listener->SetPosition(0,0,0,DS3D_IMMEDIATE);listener->Release();
                    }
                    primary->Release();
                }
                hr=buffer->QueryInterface(IID_IDirectSound3DBuffer,reinterpret_cast<void**>(&source3d));Hr("QuerySource3D",hr);
                if(SUCCEEDED(hr)) {
                    Hr("SetMinDistance",source3d->SetMinDistance(100,DS3D_IMMEDIATE));
                    Hr("SetSourcePosition",source3d->SetPosition(static_cast<float>(distance),0,0,DS3D_IMMEDIATE));source3d->Release();
                }
                printf("\"sourceDistanceUu\":%.9g,\"sourceMinDistanceUu\":100,\"listenerRolloff\":0.5,\"listenerDistanceFactor\":0.0127,",distance);
            }
            hr=buffer->QueryInterface(KsPropertySet,reinterpret_cast<void**>(&properties));Hr("QueryIKsPropertySet",hr);
            if (SUCCEEDED(hr) && properties) {
                void** propertyTable=*reinterpret_cast<void***>(properties);
                printf("\"propertySetFunction\":\"0x%08lx\",\"propertySetVendorOffset\":\"0x%08lx\",",
                    static_cast<unsigned long>(reinterpret_cast<uintptr_t>(propertyTable[4])),
                    static_cast<unsigned long>(reinterpret_cast<uintptr_t>(propertyTable[4])-reinterpret_cast<uintptr_t>(module)));
                DWORD support=0;hr=properties->QuerySupport(Eax3Listener,1,&support);Hr("EAX3QuerySupportAll",hr);printf("\"EAX3SupportBits\":%lu,",support);
                supported=SUCCEEDED(hr) && (support&KSPROPERTY_SUPPORT_SET) && (support&KSPROPERTY_SUPPORT_GET);
                if(distance>=0) {
                    DWORD flags=0,bytes=0;float rolloff=0,air=0;
                    hr=properties->Get(Eax3Source,22,nullptr,0,&flags,sizeof(flags),&bytes);Hr("GetEaxSourceFlags",hr);printf("\"eaxSourceFlags\":%lu,",flags);
                    hr=properties->Get(Eax3Source,20,nullptr,0,&rolloff,sizeof(rolloff),&bytes);Hr("GetEaxSourceRoomRolloff",hr);printf("\"eaxSourceRoomRolloff\":%.9g,",rolloff);
                    hr=properties->Get(Eax3Source,21,nullptr,0,&air,sizeof(air),&bytes);Hr("GetEaxSourceAirAbsorption",hr);printf("\"eaxSourceAirAbsorption\":%.9g,",air);
                }
                support=0;hr=properties->QuerySupport(Eax2Listener,1,&support);Hr("EAX2QuerySupportAll",hr);printf("\"EAX2SupportBits\":%lu,",support);
                GUID unknown={0xc62d9123,0x51cc,0x4397,{0xa3,0x52,0x3a,0x8a,0x6e,0x31,0xc2,0xda}};
                support=0;hr=properties->QuerySupport(unknown,1,&support);Hr("UnknownQuerySupport",hr);printf("\"UnknownSupportBits\":%lu,",support);
                BYTE returned[112]={};DWORD returnedSize=0;
                hr=properties->Get(Eax3Listener,1,nullptr,0,returned,sizeof(returned),&returnedSize);Hr("EAX3GetBefore",hr);printf("\"EAX3GetBeforeBytes\":%lu,",returnedSize);
                if(argc>=3 && !driverDefault && SUCCEEDED(hr)) {
                    std::wstring output=std::wstring(argv[2])+L".vendor-default.bin";FILE* f=nullptr;_wfopen_s(&f,output.c_str(),L"wb");if(f){fwrite(returned,1,sizeof(returned),f);fclose(f);}
                }
                if (argc>=3) {
                    FILE* input=nullptr;if(!driverDefault)_wfopen_s(&input,argv[2],L"rb");
                    BYTE block[112]={};size_t read=input?fread(block,1,sizeof(block),input):0;if(input)fclose(input);
                    if(driverDefault) { memcpy(block,returned,sizeof(block));read=sizeof(block); }
                    printf("\"inputBytes\":%zu,",read);
                    if(read==sizeof(block)) {
                        if(driverDefault) { printf("\"driverDefaultNoSetter\":true,");blockApplied=true; }
                        else { hr=properties->Set(Eax3Listener,1,nullptr,0,block,sizeof(block));Hr("EAX3SetAll",hr);blockApplied=SUCCEEDED(hr); }
                        returnedSize=0;hr=properties->Get(Eax3Listener,1,nullptr,0,returned,sizeof(returned),&returnedSize);Hr("EAX3GetAfter",hr);printf("\"EAX3GetAfterBytes\":%lu,",returnedSize);
                        size_t different=0;for(size_t i=0;i<sizeof(block);++i)if(block[i]!=returned[i])++different;
                        printf("\"returnedDifferentBytes\":%zu,",different);
                        if(!driverDefault) { std::wstring output=std::wstring(argv[2])+L".vendor-returned.bin";FILE* f=nullptr;_wfopen_s(&f,output.c_str(),L"wb");if(f){fwrite(returned,1,sizeof(returned),f);fclose(f);} }
                        if(sequentialOnly) {
                            BYTE nextBlock[112]={};FILE* next=nullptr;_wfopen_s(&next,argv[3]+7,L"rb");const size_t nextRead=next?fread(nextBlock,1,sizeof(nextBlock),next):0;if(next)fclose(next);
                            if(nextRead==sizeof(nextBlock)) {
                                hr=properties->Set(Eax3Listener,1,nullptr,0,nextBlock,sizeof(nextBlock));Hr("EAX3SequentialSet",hr);
                                hr=properties->Get(Eax3Listener,1,nullptr,0,returned,sizeof(returned),&returnedSize);Hr("EAX3SequentialGet",hr);
                                printf("\"previousBlockRetained\":%s,",memcmp(block,returned,sizeof(block))==0?"true":"false");
                            }
                        }
                        if(argc>=6 && argv[5][0]!=L'-') {
                            struct Occlusion { LONG gain;float lfRatio;float roomRatio;float directRatio; } sourceInput={-2000,.25f,1.5f,1.0f},value={};
                            DWORD bytes=0;hr=properties->Get(Eax3Source,3,nullptr,0,&value,sizeof(value),&bytes);Hr("EAX3SourceGetBefore",hr);
                            printf("\"sourceBefore\":{\"occlusion\":%ld,\"lfRatio\":%.9g,\"roomRatio\":%.9g,\"directRatio\":%.9g,\"bytes\":%lu},",value.gain,value.lfRatio,value.roomRatio,value.directRatio,bytes);
                            const DWORD requested=static_cast<DWORD>(_wtoi(argv[5]));
                            const DWORD size=requested==4?4:16;
                            hr=properties->Set(Eax3Source,3,nullptr,0,&sourceInput,size);Hr("EAX3SourceSetOcclusion",hr);printf("\"sourceSetBytes\":%lu,",size);
                            bytes=0;hr=properties->Get(Eax3Source,3,nullptr,0,&value,sizeof(value),&bytes);Hr("EAX3SourceGetAfter",hr);
                            printf("\"sourceAfter\":{\"occlusion\":%ld,\"lfRatio\":%.9g,\"roomRatio\":%.9g,\"directRatio\":%.9g,\"bytes\":%lu},",value.gain,value.lfRatio,value.roomRatio,value.directRatio,bytes);
                        }
                        if(argc>=4 && !sequentialOnly && blockApplied && SUCCEEDED(hr) && different==0) {
                            ProcessLoopbackCapture capture;
                            hr=capture.Start();Hr("ProcessLoopbackStart",hr);
                            if(SUCCEEDED(hr)) {
                                void* first=nullptr;void* second=nullptr;DWORD firstBytes=0,secondBytes=0;
                                hr=buffer->Lock(0,desc.dwBufferBytes,&first,&firstBytes,&second,&secondBytes,DSBLOCK_ENTIREBUFFER);Hr("BufferLock",hr);
                                if(SUCCEEDED(hr)) {
                                    memset(first,0,firstBytes);if(second)memset(second,0,secondBytes);
                                    // One sample at amplitude 0.1, delayed 250 ms to keep start-up latency out of the IR.
                                    if(!lateImpulse)static_cast<int16_t*>(first)[12000]=3277;
                                    if(doubleImpulse||lateImpulse)static_cast<int16_t*>(first)[secondFrame]=3277;
                                    if(tone) for(int frame=24000;frame<static_cast<int>(firstBytes/2);++frame) {
                                        const double t=(frame-24000)/48000.0;
                                        const double sample=.01*(sin(6.283185307179586*997*t)+sin(6.283185307179586*2399*t)+sin(6.283185307179586*349*t));
                                        static_cast<int16_t*>(first)[frame]=static_cast<int16_t>(std::lround(sample*32768));
                                    }
                                    buffer->Unlock(first,firstBytes,second,secondBytes);
                                    capture.Poll(100);hr=buffer->Play(0,0,0);Hr("PlayImpulse",hr);played=SUCCEEDED(hr);
                                    const DWORD duration=static_cast<DWORD>(1000*(argc>=5?_wtof(argv[4]):8.0));
                                    const ULONGLONG started=GetTickCount64(),end=started+duration;bool transitioned=false;
                                    while(GetTickCount64()<end && SUCCEEDED(hr)) {
                                        hr=capture.Poll(10);
                                        if(transition && !transitioned && GetTickCount64()-started>=transitionMs) {
                                            BYTE nextBlock[112]={};FILE* next=nullptr;_wfopen_s(&next,transition,L"rb");const size_t nextRead=next?fread(nextBlock,1,sizeof(nextBlock),next):0;if(next)fclose(next);
                                            if(nextRead==sizeof(nextBlock)) {
                                                DWORD play=0,write=0;buffer->GetCurrentPosition(&play,&write);
                                                printf("\"transitionPlayCursorFrames\":%lu,\"transitionWriteCursorFrames\":%lu,\"transitionCapturedFrames\":%zu,\"transitionElapsedMilliseconds\":%llu,",play/2,write/2,capture.SampleCount()/2,GetTickCount64()-started);
                                                hr=properties->Set(Eax3Listener,1,nullptr,0,nextBlock,sizeof(nextBlock));Hr("EAX3TransitionSet",hr);
                                            }
                                            transitioned=true;
                                        }
                                    }
                                    Hr("ProcessLoopbackCapture",hr);buffer->Stop();capture.Write(argv[3]);
                                    printf("\"capturedFrames\":%zu,\"capturedPeak\":%.12g,",capture.SampleCount()/2,capture.Peak());
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    printf("\"eax3PropertiesSupported\":%s,\"modules\":[",supported?"true":"false");
    HANDLE snap=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,0);MODULEENTRY32W entry={};entry.dwSize=sizeof(entry);bool first=true;
    if (snap!=INVALID_HANDLE_VALUE && Module32FirstW(snap,&entry)) do {
        std::wstring name(entry.szModule);
        for(auto& c:name)c=towlower(c);
        if (name.find(L"dsound")!=std::wstring::npos || name.find(L"openal")!=std::wstring::npos || name.find(L"eax")!=std::wstring::npos || name.find(L"oal")!=std::wstring::npos || name.find(L"dmo")!=std::wstring::npos) {
            if (!first)putchar(',');first=false;JsonString(entry.szExePath);
        }
    } while(Module32NextW(snap,&entry));
    if(snap!=INVALID_HANDLE_VALUE)CloseHandle(snap);
    printf("],\"audioPlayed\":%s,\"captureTarget\":\"self-process-tree-only\",\"microphoneOpened\":false}\n",played?"true":"false");
    if(properties)properties->Release();if(buffer)buffer->Release();if(ds)ds->Release();
    FreeLibrary(module);if(SUCCEEDED(com))CoUninitialize();
    return supported && blockApplied?0:4;
}
