typedef struct SoundEventNames SoundEventNames;
typedef struct NameArgument { const char *value; } NameArgument;
unsigned long __cdecl sound_get_event_name_support(void);
unsigned long __fastcall sound_lookup_event_name(SoundEventNames *, NameArgument);
void __stdcall SND_fn_vEnterCriticalSectionThreadSnd_(void);
void __stdcall SND_fn_vMouchardThreadsnd(const char *source, unsigned long line);
void __stdcall SND_fn_vQuitCriticalSectionThreadSnd(void);
void __stdcall SND_fn_vStrncpy(char *destination, const char *source, unsigned long capacity);
void __stdcall SND_fn_vDisplayErrorEx(int code, const char *context, unsigned long category);

extern SoundEventNames *sound_event_names;

unsigned long __stdcall SND_fn_ulGetEditorIdFromSoundEventName(const char *name)
{
    char message[256];
    unsigned long id;
    NameArgument key = { name };
    SND_fn_vEnterCriticalSectionThreadSnd_();
    SND_fn_vMouchardThreadsnd((const char *)0x11173078, 0x53b);
    if (!sound_get_event_name_support()) {
        SND_fn_vQuitCriticalSectionThreadSnd();
        SND_fn_vStrncpy(message, (const char *)0x111731d0, sizeof(message));
        SND_fn_vDisplayErrorEx(8, message, 3);
        return (unsigned long)-1;
    }
    id = sound_lookup_event_name(sound_event_names, key);
    SND_fn_vQuitCriticalSectionThreadSnd();
    return id;
}
