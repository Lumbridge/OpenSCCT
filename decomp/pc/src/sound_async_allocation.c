#include "sound_async_worker.h"
void *__stdcall SND_fn_pvMallocSnd(Word size);
void __stdcall SND_fn_vFreeSnd(void *allocation);
__declspec(dllimport) Handle __stdcall CreateMutexA(void *attributes, int owned, const char *name);
__declspec(dllimport) int __stdcall CloseHandle(Handle handle);

AsyncRequest *__cdecl sound_alloc_stream_request(Word bytes)
{
    AsyncRequest *request = SND_fn_pvMallocSnd(sizeof(AsyncRequest));
    if (!request)
        goto failed;
    request->mutex = CreateMutexA(0, 0, 0);
    if (!request->mutex)
        goto free_request;
    request->buffer = SND_fn_pvMallocSnd(bytes);
    if (!request->buffer)
        goto close_mutex;
    return request;

close_mutex:
    CloseHandle(request->mutex);
free_request:
    SND_fn_vFreeSnd(request);
failed:
    return 0;
}

void __cdecl sound_dispose_stream_request(AsyncRequest *request)
{
    SND_fn_vFreeSnd(request->buffer);
    request->buffer = 0;
    CloseHandle(request->mutex);
    SND_fn_vFreeSnd(request);
}
