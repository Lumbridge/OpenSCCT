#include "sound_stream_request.h"

void __stdcall SND_fn_vAbortLastStreamAsyncSnd(struct SoundStreamRequest *stream)
{
    if (!stream || stream == (void *)-1)
        return;
    stream->state = 0;
    stream->progress = 0;
    stream->request_start = 0;
    stream->size = 0;
    stream->queued = 0;
    stream->pending[0] = 0;
    stream->pending[1] = 0;
    stream->pending[2] = 0;
}

int __stdcall SND_fn_bIsLastStreamAsyncDoneSnd(struct SoundStreamRequest *stream)
{
    if (!stream || stream == (void *)-1) return 0;
    return stream->state == 2;
}
