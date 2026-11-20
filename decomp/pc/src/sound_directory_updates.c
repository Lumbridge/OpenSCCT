/* Reconstructed contracts of the campaign directory module. The build's
   named procedure exports identify the helpers used by these routines. */
void __stdcall SND_fn_vEnterCriticalSectionThreadSnd_(void);
void __stdcall SND_fn_vQuitCriticalSectionThreadSnd(void);
void __stdcall SND_fn_vMouchardThreadsnd(const char *file, unsigned line);
void __stdcall SND_fn_vStrncpy(char *dest, const char *src, unsigned capacity);

extern char sound_soft_directory[256];
extern char sound_current_language_directory[9];
extern const char sound_directory_source[];
extern unsigned long sound_partial_directory_count;

unsigned long __stdcall SND_fn_ulGetNumberOfPartialDirectory(void)
{
    unsigned long total;
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 363);
    total = sound_partial_directory_count + 1;
    SND_fn_vQuitCriticalSectionThreadSnd();
    return total;
}

void __stdcall SND_fn_vSetSoftDirectory(const char *directory)
{
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd(sound_directory_source, 496);
    SND_fn_vStrncpy(sound_soft_directory, directory, sizeof(sound_soft_directory));
    SND_fn_vQuitCriticalSectionThreadSnd();
}

void __stdcall SND_fn_vSetCurrentLangDirectory(const char *directory)
{
    SND_fn_vStrncpy(sound_current_language_directory, directory, sizeof(sound_current_language_directory));
}
