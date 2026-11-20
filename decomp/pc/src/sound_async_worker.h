#ifndef SOUND_ASYNC_WORKER_H
#define SOUND_ASYNC_WORKER_H

typedef unsigned long Word;
typedef void *Handle;
typedef struct AsyncRequest {
    Handle mutex;
    int done;
    unsigned char reserved[4];
    Handle file;
    char path[260];
    void *destination;
    void *buffer;
    Word size;
    unsigned long start;
    Word transferred;
} AsyncRequest;

typedef struct StreamWorker {
    Handle thread;
    Word unknown;
    Handle mutex;
    unsigned char reserved[8];
    Handle io_ready;
    Handle wake;
    Handle event_1c;
    Handle event_20;
    Handle stop;
} StreamWorker;

typedef struct AsyncFile {
    char path[260];
    Word initialized;
    Handle file;
    AsyncRequest *request;
    StreamWorker *worker;
    struct AsyncFile *next;
} AsyncFile;

#endif
