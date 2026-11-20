/* Named KERNEL32 import slots in the Steam 252084 campaign image. */
typedef int (__stdcall *CloseHandleFn)(void *file);
typedef int (__stdcall *DeleteFileFn)(const char *path);
#define CLOSE_HANDLE (*(CloseHandleFn const *)0x110b738c)
#define DELETE_FILE (*(DeleteFileFn const *)0x110b7378)

void __stdcall SND_fn_vCloseFileSnd(void *file)
{
    CLOSE_HANDLE(file);
}

int __stdcall SND_fn_bDeleteFile(const char *path)
{
    return DELETE_FILE(path);
}
