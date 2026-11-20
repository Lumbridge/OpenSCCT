typedef unsigned long DWord;
typedef int (__stdcall *ReadFileFn)(void *file, void *buffer, DWord count, DWord *read, void *overlapped);
typedef int (__stdcall *WriteFileFn)(void *file, const void *buffer, DWord count, DWord *written, void *overlapped);
typedef DWord (__stdcall *GetLastErrorFn)(void);
typedef DWord (__stdcall *GetFileSizeFn)(void *file, DWord *high);

#define READ_FILE (*(ReadFileFn const *)0x110b72cc)
#define WRITE_FILE (*(WriteFileFn const *)0x110b73c8)
#define GET_LAST_ERROR (*(GetLastErrorFn const *)0x110b73b4)
#define GET_FILE_SIZE (*(GetFileSizeFn const *)0x110b73b8)

DWord __stdcall SND_fn_ulReadFileSnd(void *file, DWord count, void *buffer)
{
    DWord read = 0;
    if (!READ_FILE(file, buffer, count, &read, 0)) {
        GET_LAST_ERROR();
        return 0;
    }
    return read;
}

DWord __stdcall SND_fn_ulWriteFileSnd(void *file, DWord count, const void *buffer)
{
    DWord written = 0;
    if (!WRITE_FILE(file, buffer, count, &written, 0)) {
        GET_LAST_ERROR();
        return 0;
    }
    return written;
}

DWord __stdcall SND_fn_ulGetFileSize(void *file)
{
    return GET_FILE_SIZE(file, 0);
}
