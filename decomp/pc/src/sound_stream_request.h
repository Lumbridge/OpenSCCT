#ifndef SCCT_SOUND_STREAM_REQUEST_H
#define SCCT_SOUND_STREAM_REQUEST_H

typedef struct SoundStreamRequest {
    unsigned long state;
    void *file;
    unsigned long progress;
    unsigned long size;
    unsigned long request_start;
    unsigned long queued;
    void *buffer;
    unsigned long capacity;
    unsigned long pending[3];
} SoundStreamRequest;

#endif
