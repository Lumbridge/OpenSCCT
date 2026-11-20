typedef struct FindDataA {
    unsigned char data[320];
} FindDataA;
typedef void *(__stdcall *FindFirstFileFn)(const char *path, FindDataA *data);
typedef int (__stdcall *FindCloseFn)(void *handle);
#define FIND_FIRST_FILE (*(FindFirstFileFn const *)0x110b72a0)
#define FIND_CLOSE (*(FindCloseFn const *)0x110b729c)

int __stdcall SND_fn_bTestFileExistSnd(const char *path)
{
    FindDataA data;
    void *handle = FIND_FIRST_FILE(path, &data);
    if (handle == (void *)-1)
        return 0;
    FIND_CLOSE(handle);
    return 1;
}
