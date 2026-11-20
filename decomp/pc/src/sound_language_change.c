extern char sound_current_language_directory[9];
extern const char sound_directory_source[];
void __stdcall SND_fn_vEnterCriticalSectionThreadSnd_(void);
void __stdcall SND_fn_vQuitCriticalSectionThreadSnd(void);
void __stdcall SND_fn_vMouchardThreadsnd(const char *file, unsigned line);
void __stdcall SND_fn_vStrncpy(char *dest, const char *src, unsigned capacity);
long __stdcall SND_fn_lStriCmp(const char *left, const char *right);
void __cdecl sound_on_language_changed(void);
int __cdecl strcmp(const char *left, const char *right);

void __stdcall SND_fn_vSetCurrentLanguage(const char *name)
{
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 516);
    if (SND_fn_lStriCmp(sound_current_language_directory, name)) {
        if (strcmp(sound_current_language_directory, "") == 0) {
            SND_fn_vStrncpy(sound_current_language_directory, name, sizeof(sound_current_language_directory));
        } else {
            SND_fn_vStrncpy(sound_current_language_directory, name, sizeof(sound_current_language_directory));
            sound_on_language_changed();
        }
    }
    SND_fn_vQuitCriticalSectionThreadSnd();
}
