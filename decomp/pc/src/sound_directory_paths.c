/* Steam campaign audio search paths. Directory nodes begin with an inline
   path, followed by links at offsets 0x100 and 0x104 on the x86 target. */
struct DirectoryNode {
    char path[256];
    struct DirectoryNode *next;
};

extern char sound_master_directory[256];
extern struct DirectoryNode *sound_first_directory;
extern unsigned long sound_partial_directory_count;
extern const char sound_directory_source[];
extern const char sound_null_master_output[];
extern const char sound_null_partial_output[];
extern const char sound_invalid_partial_index[];

void __stdcall SND_fn_vEnterCriticalSectionThreadSnd_(void);
void __stdcall SND_fn_vQuitCriticalSectionThreadSnd(void);
void __stdcall SND_fn_vMouchardThreadsnd(const char *file, unsigned line);
void __stdcall SND_fn_vStrncpy(char *dest, const char *src, unsigned capacity);
void __stdcall SND_fn_vDisplayError(unsigned code, const char *message);

int __stdcall SND_fn_bGetMasterDirectory(char *dest, unsigned capacity)
{
    if (!dest) {
        SND_fn_vDisplayError(47, sound_null_master_output);
        return 0;
    }
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 146);
    SND_fn_vStrncpy(dest, sound_master_directory, capacity);
    SND_fn_vQuitCriticalSectionThreadSnd();
    return 1;
}

int __stdcall SND_fn_bGetOnePartialDirectory(long index, char *dest, unsigned capacity)
{
    struct DirectoryNode *node;
    if (!dest) {
        SND_fn_vDisplayError(8, sound_null_partial_output);
        return 0;
    }
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 257);
    if ((unsigned long)index < sound_partial_directory_count) {
        node = sound_first_directory;
        while (index > 0) {
            --index;
            node = node->next;
        }
        SND_fn_vStrncpy(dest, node->path, capacity);
        SND_fn_vQuitCriticalSectionThreadSnd();
        return 1;
    }
    SND_fn_vDisplayError(8, sound_invalid_partial_index);
    SND_fn_vQuitCriticalSectionThreadSnd();
    return 0;
}
