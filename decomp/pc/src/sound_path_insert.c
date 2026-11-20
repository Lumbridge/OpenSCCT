/* Each audio search path carries intrusive links and an optional owner list. */
struct SoundPath;
struct SoundPathList {
    struct SoundPath *first;
    struct SoundPath *last;
    unsigned count;
};

struct SoundPath {
    char name[256];
    struct SoundPath *next;
    struct SoundPath *previous;
    struct SoundPathList *owner;
};

extern struct SoundPathList sound_paths;
extern char sound_master_directory[256];
extern const char sound_directory_source[];

void __stdcall SND_fn_vEnterCriticalSectionThreadSnd_(void);
void __stdcall SND_fn_vQuitCriticalSectionThreadSnd(void);
void __stdcall SND_fn_vMouchardThreadsnd(const char *file, unsigned line);
void __stdcall SND_fn_vMemsetSnd(void *dest, int fill, unsigned length);
void __stdcall SND_fn_vFreeSnd(void *pointer);
long __stdcall SND_fn_lStriCmp(const char *left, const char *right);


extern const char sound_null_partial_input[];
extern const char sound_missing_master_path[];
extern const char sound_insufficient_path_memory[];
extern const char sound_partial_missing_separator[];
unsigned __cdecl strlen(const char *);
char *__cdecl strcpy(char *dest, const char *src);
void __stdcall SND_fn_vDisplayError(unsigned code, const char *message);
void *__stdcall SND_fn_pvMallocSnd(unsigned size);
char __stdcall SND_fn_cGetDirectorySeparator(void);
int __stdcall SND_fn_bIsDataDirectory(const char *name);
void __stdcall SND_fn_vStrncpy(char *dest, const char *src, unsigned capacity);

void __stdcall SND_fn_vAddPartialDirectory(const char *name)
{
    int length, i;
    char *copy;
    struct SoundPath *node;
    if (!name) {
        SND_fn_vDisplayError(47, sound_null_partial_input);
        return;
    }
    if (strlen(sound_master_directory) == 0) {
        SND_fn_vDisplayError(47, sound_missing_master_path);
        return;
    }
    length = strlen(name);
    copy = SND_fn_pvMallocSnd(length + 1);
    if (!copy) {
        SND_fn_vDisplayError(47, sound_insufficient_path_memory);
        return;
    }
    strcpy(copy, name);
    for (i = 0; i < length; ++i) {
        if (copy[i] == '/' || copy[i] == '\\')
            copy[i] = SND_fn_cGetDirectorySeparator();
    }
    if (length > 0 && copy[length - 1] != SND_fn_cGetDirectorySeparator()) {
        SND_fn_vDisplayError(47, sound_partial_missing_separator);
        SND_fn_vFreeSnd(copy);
        return;
    }
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 215);
    if (!SND_fn_bIsDataDirectory(copy)) {
        node = SND_fn_pvMallocSnd(sizeof(*node));
        if (node) {
            SND_fn_vStrncpy(node->name, copy, sizeof(node->name));
            node->previous = 0;
            node->owner = 0;
            node->next = 0;
            node->previous = sound_paths.last;
            node->owner = &sound_paths;
            if (sound_paths.first == 0)
                sound_paths.first = node;
            else
                sound_paths.last->next = node;
            sound_paths.last = node;
            ++sound_paths.count;
        }
    }
    SND_fn_vQuitCriticalSectionThreadSnd();
    SND_fn_vFreeSnd(copy);
}
