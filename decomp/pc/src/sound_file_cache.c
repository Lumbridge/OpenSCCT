typedef struct CacheEntry {
    char path[260];
    void *handle;
} CacheEntry;
typedef struct CacheLock CacheLock;
extern CacheLock cache_lock;
extern CacheEntry cache_entries[4];
__declspec(dllimport) void __stdcall EnterCriticalSection(CacheLock *lock);
__declspec(dllimport) void __stdcall LeaveCriticalSection(CacheLock *lock);
void __stdcall SND_fn_vMemsetSnd(void *destination, int byte, unsigned long length);

void __cdecl sound_reset_file_cache(void)
{
    long i;
    EnterCriticalSection(&cache_lock);
    for (i = 0; i < 4; ++i) {
        cache_entries[i].handle = (void *)-1;
        SND_fn_vMemsetSnd(cache_entries[i].path, 0, sizeof(cache_entries[i].path));
    }
    LeaveCriticalSection(&cache_lock);
}
