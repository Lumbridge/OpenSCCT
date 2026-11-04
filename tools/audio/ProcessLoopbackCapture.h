// Capture only this harness process tree using Microsoft's process-loopback API.
// No microphone or whole-device loopback endpoint is opened.
#pragma once
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <propidl.h>
#include <atomic>
#include <fstream>
#include <vector>

class ActivationCallback final : public IActivateAudioInterfaceCompletionHandler, public IAgileObject {
    std::atomic<ULONG> references{1};
public:
    HANDLE complete=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    HRESULT result=E_PENDING;
    IAudioClient* client=nullptr;
    ~ActivationCallback() { if(client)client->Release();CloseHandle(complete); }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { auto count=--references;if(!count)delete this;return count; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id,void** out) override {
        if(!out)return E_POINTER;*out=nullptr;
        if(id==__uuidof(IUnknown)||id==__uuidof(IActivateAudioInterfaceCompletionHandler))*out=static_cast<IActivateAudioInterfaceCompletionHandler*>(this);
        else if(id==__uuidof(IAgileObject))*out=static_cast<IAgileObject*>(this);
        else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    HRESULT STDMETHODCALLTYPE ActivateCompleted(IActivateAudioInterfaceAsyncOperation* operation) override {
        IUnknown* unknown=nullptr;HRESULT activate=E_FAIL;
        result=operation->GetActivateResult(&activate,&unknown);
        if(SUCCEEDED(result))result=activate;
        if(SUCCEEDED(result) && unknown)result=unknown->QueryInterface(__uuidof(IAudioClient),reinterpret_cast<void**>(&client));
        if(unknown)unknown->Release();SetEvent(complete);return S_OK;
    }
};

class ProcessLoopbackCapture {
    IAudioClient* client=nullptr;
    IAudioCaptureClient* capture=nullptr;
    HANDLE ready=CreateEventW(nullptr,FALSE,FALSE,nullptr);
    std::vector<float> pcm;
public:
    ~ProcessLoopbackCapture() { if(client)client->Stop();if(capture)capture->Release();if(client)client->Release();CloseHandle(ready); }
    HRESULT Start() {
        auto callback=new ActivationCallback;
        AUDIOCLIENT_ACTIVATION_PARAMS params={};params.ActivationType=AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
        params.ProcessLoopbackParams.TargetProcessId=GetCurrentProcessId();
        params.ProcessLoopbackParams.ProcessLoopbackMode=PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
        PROPVARIANT variant={};variant.vt=VT_BLOB;variant.blob.cbSize=sizeof(params);variant.blob.pBlobData=reinterpret_cast<BYTE*>(&params);
        IActivateAudioInterfaceAsyncOperation* operation=nullptr;
        HRESULT hr=ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,__uuidof(IAudioClient),&variant,callback,&operation);
        if(SUCCEEDED(hr)) {
            if(WaitForSingleObject(callback->complete,10000)!=WAIT_OBJECT_0)hr=HRESULT_FROM_WIN32(WAIT_TIMEOUT);
            else hr=callback->result;
        }
        if(SUCCEEDED(hr)){client=callback->client;client->AddRef();}
        if(operation)operation->Release();callback->Release();
        if(FAILED(hr))return hr;
        WAVEFORMATEX format={};format.wFormatTag=WAVE_FORMAT_IEEE_FLOAT;format.nChannels=2;format.nSamplesPerSec=48000;
        format.wBitsPerSample=32;format.nBlockAlign=8;format.nAvgBytesPerSec=384000;
        hr=client->Initialize(AUDCLNT_SHAREMODE_SHARED,AUDCLNT_STREAMFLAGS_LOOPBACK|AUDCLNT_STREAMFLAGS_EVENTCALLBACK|AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,0,0,&format,nullptr);
        if(FAILED(hr))return hr;
        hr=client->SetEventHandle(ready);if(FAILED(hr))return hr;
        hr=client->GetService(__uuidof(IAudioCaptureClient),reinterpret_cast<void**>(&capture));if(FAILED(hr))return hr;
        return client->Start();
    }
    HRESULT Poll(DWORD waitMs) {
        WaitForSingleObject(ready,waitMs);
        UINT32 frames=0;HRESULT hr=capture->GetNextPacketSize(&frames);
        while(SUCCEEDED(hr) && frames) {
            BYTE* data=nullptr;DWORD flags=0;UINT64 position=0,qpc=0;
            hr=capture->GetBuffer(&data,&frames,&flags,&position,&qpc);if(FAILED(hr))return hr;
            size_t offset=pcm.size();pcm.resize(offset+frames*2);
            if(!(flags&AUDCLNT_BUFFERFLAGS_SILENT))memcpy(pcm.data()+offset,data,frames*8);
            capture->ReleaseBuffer(frames);
            hr=capture->GetNextPacketSize(&frames);
        }
        return hr;
    }
    size_t SampleCount() const { return pcm.size(); }
    double Peak() const { double peak=0;for(float v:pcm)peak=std::max(peak,std::abs(static_cast<double>(v)));return peak; }
    void Write(const wchar_t* path) {
        client->Stop();Poll(0);
        std::ofstream file(path,std::ios::binary);
        auto u16=[&](uint16_t v){file.write(reinterpret_cast<const char*>(&v),2);};
        auto u32=[&](uint32_t v){file.write(reinterpret_cast<const char*>(&v),4);};
        file.write("RIFF",4);u32(static_cast<uint32_t>(36+pcm.size()*4));file.write("WAVEfmt ",8);u32(16);
        u16(WAVE_FORMAT_IEEE_FLOAT);u16(2);u32(48000);u32(384000);u16(8);u16(32);file.write("data",4);u32(static_cast<uint32_t>(pcm.size()*4));
        file.write(reinterpret_cast<const char*>(pcm.data()),pcm.size()*4);
    }
};
