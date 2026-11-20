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

static __forceinline void remove_sound_path(struct SoundPath *path)
{
    if (path->next)
        path->next->previous = path->previous;
    if (path->previous)
        path->previous->next = path->next;
    if (path->owner) {
        --path->owner->count;
        if (path->owner->first == path)
            path->owner->first = path->next;
        if (path->owner->last == path)
            path->owner->last = path->previous;
    }
    path->next = 0;
    path->previous = 0;
    path->owner = 0;
    SND_fn_vFreeSnd(path);
}

void __stdcall SND_fn_vRemovePartialDirectory(const char *name)
{
    struct SoundPath *node;
    struct SoundPath *next;
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 297);
    node = sound_paths.first;
    if (node)
        next = node->next;
    while (node) {
        if (!SND_fn_lStriCmp(node->name, name)) {
            remove_sound_path(node);
            break;
        }
        node = next;
        if (node)
            next = node->next;
    }
    SND_fn_vQuitCriticalSectionThreadSnd();
}

void __stdcall SND_fn_vPurgeAllDirectories(void)
{
    struct SoundPath *node;
    struct SoundPath *next;
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 328);
    SND_fn_vMemsetSnd(sound_master_directory, 0, sizeof(sound_master_directory));
    node = sound_paths.first;
    if (node)
        next = node->next;
    while (node) {
        remove_sound_path(node);
        node = next;
        if (node)
            next = node->next;
    }
    SND_fn_vQuitCriticalSectionThreadSnd();
}
