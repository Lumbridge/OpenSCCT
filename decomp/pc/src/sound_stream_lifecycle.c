#include "sound_stream_request.h"

void __cdecl sound_close_stream_file(void *file);
int __cdecl sound_submit_file_read(void *file, unsigned long count, unsigned long start, void *destination);
void __stdcall SND_fn_vFreeSndAligned(void *aligned, unsigned long alignment);
void __stdcall SND_fn_vFreeSnd(void *allocation);

int __cdecl sound_submit_stream_read(SoundStreamRequest *stream, unsigned long count, unsigned long start, void *destination)
{
    int accepted = sound_submit_file_read(stream->file, count, start, destination);
    if (accepted)
        stream->queued = count;
    return accepted;
}

void __stdcall SND_fn_vStartStreamAsyncSnd(SoundStreamRequest *stream, unsigned long start, unsigned long size)
{
    unsigned long chunk;
    if (!stream || stream == (SoundStreamRequest *)-1 || size <= 0)
        return;
    if (stream->state != 0 && stream->state != 2)
        return;

    stream->queued = 0;
    stream->progress = 0;
    stream->request_start = start;
    stream->size = size;
    stream->pending[0] = 0;
    stream->pending[1] = 0;
    stream->pending[2] = 0;
    chunk = stream->capacity < size ? stream->capacity : size;
    if (chunk > 0 && sound_submit_stream_read(stream, chunk, start, stream->buffer))
        stream->state = 1;
}

void __stdcall SND_fn_vDestroyStreamAsyncSnd(SoundStreamRequest *stream)
{
    if (stream && stream != (SoundStreamRequest *)-1) {
        stream->state = 0;
        stream->progress = 0;
        stream->request_start = 0;
        stream->size = 0;
        stream->queued = 0;
        stream->pending[0] = 0;
        stream->pending[1] = 0;
        stream->pending[2] = 0;
        sound_close_stream_file(stream->file);
        if (stream->buffer)
            SND_fn_vFreeSndAligned(stream->buffer, 32);
        SND_fn_vFreeSnd(stream);
    }
}
