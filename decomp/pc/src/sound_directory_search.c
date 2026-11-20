/* Campaign audio searches include the master path, each partial path and
   a language subdirectory of each when a language is configured. */
struct SoundDirNode {
    char path[256];
    struct SoundDirNode *next;
};
extern struct SoundDirNode *sound_first_directory;
extern char sound_master_directory[256];
extern char sound_current_language_directory[9];
extern const char sound_directory_source[];
extern const char sound_empty_text[];
extern const char sound_path_size_message[];
void __stdcall SND_fn_vEnterCriticalSectionThreadSnd_(void);
void __stdcall SND_fn_vQuitCriticalSectionThreadSnd(void);
void __stdcall SND_fn_vMouchardThreadsnd(const char *file, unsigned line);
void __stdcall SND_fn_vStrncpy(char *dest, const char *src, unsigned capacity);
long __stdcall SND_fn_lStriCmp(const char *left, const char *right);
char __stdcall SND_fn_cGetDirectorySeparator(void);
void __stdcall SND_fn_vDisplayError(unsigned code, const char *message);
unsigned __cdecl strlen(const char *text);
char *__cdecl strcat(char *dest, const char *src);

int __stdcall SND_fn_bIsDataDirectory(const char *name)
{
    char localized[256];
    unsigned length;
    struct SoundDirNode *node;
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 390);
    if (!SND_fn_lStriCmp(sound_master_directory, name)) {
        SND_fn_vQuitCriticalSectionThreadSnd();
        return 1;
    }
    if (SND_fn_lStriCmp(sound_current_language_directory, sound_empty_text)) {
        SND_fn_vStrncpy(localized, sound_master_directory, sizeof(localized));
        if (strlen(localized) + 12 < sizeof(localized)) {
            strcat(localized, sound_current_language_directory);
            length = strlen(localized);
            localized[length] = SND_fn_cGetDirectorySeparator();
            localized[length + 1] = 0;
            if (!SND_fn_lStriCmp(localized, name)) {
                SND_fn_vQuitCriticalSectionThreadSnd();
                return 1;
            }
        } else {
            SND_fn_vQuitCriticalSectionThreadSnd();
            SND_fn_vDisplayError(10, sound_path_size_message);
            return 0;
        }
    }

    node = sound_first_directory;
    if (sound_first_directory) {
        do {
            if (!SND_fn_lStriCmp(node->path, name)) {
                SND_fn_vQuitCriticalSectionThreadSnd();
                return 1;
            }
            if (SND_fn_lStriCmp(sound_current_language_directory, sound_empty_text)) {
                SND_fn_vStrncpy(localized, node->path, sizeof(localized));
                if (strlen(localized) + 12 < sizeof(localized)) {
                    strcat(localized, sound_current_language_directory);
                    length = strlen(localized);
                    localized[length] = SND_fn_cGetDirectorySeparator();
                    localized[length + 1] = 0;
                    if (!SND_fn_lStriCmp(localized, name)) {
                        SND_fn_vQuitCriticalSectionThreadSnd();
                        return 1;
                    }
                } else {
                    SND_fn_vQuitCriticalSectionThreadSnd();
                    SND_fn_vDisplayError(10, sound_path_size_message);
                    return 0;
                }
            }
            node = node->next;
        } while (node);
    }
    SND_fn_vQuitCriticalSectionThreadSnd();
    return 0;
}
