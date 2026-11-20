struct SoundStreamRequest {
    unsigned long state;
    void *file;
    unsigned long progress;
    unsigned long size;
    void *destination;
    unsigned long consumed;
    void *buffer;
    unsigned long capacity;
    unsigned long pending[3];
};

void __stdcall SND_fn_vAbortLastStreamAsyncSnd(struct SoundStreamRequest *stream)
{
    if (!stream || stream == (void *)-1)
        return;
    stream->state = 0;
    stream->progress = 0;
    stream->destination = 0;
    stream->size = 0;
    stream->consumed = 0;
    stream->pending[0] = 0;
    stream->pending[1] = 0;
    stream->pending[2] = 0;
}

int __stdcall SND_fn_bIsLastStreamAsyncDoneSnd(struct SoundStreamRequest *stream)
{
    if (!stream || stream == (void *)-1) return 0;
    return stream->state == 2;
}
