typedef void (__cdecl *ExitCallback)(void);
extern ExitCallback __cdecl _onexit(ExitCallback callback);
int __cdecl atexit(ExitCallback callback)
{
    return _onexit(callback) ? 0 : -1;
}
