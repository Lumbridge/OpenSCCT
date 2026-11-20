typedef struct {
    unsigned id, type;
    unsigned unknown08, field0c;
    unsigned gap10[5], field24;
    void *field28;
} SoundResource;
extern void __stdcall EnterCriticalSectionTool_(void);
extern void __stdcall MouchardThreadTool(const char *site,int line);
extern void __stdcall QuitCriticalSectionTool(void);
extern const char MixerLockSite[];
extern void __stdcall SND_fn_vFreeSnd(void *buffer);
extern void __cdecl release_sample(SoundResource *resource, int full);

#pragma code_seg(".text$V759230")
void __cdecl release_sound_resource(SoundResource *resource)
{
    if (!resource)
        return;
    EnterCriticalSectionTool_();
    MouchardThreadTool(MixerLockSite, 0xdcb);
    switch (resource->type) {
    case 1:
        release_sample(resource, 1);
        break;
    case 6:
        if (resource->field0c && !resource->field24 && resource->field28)
            SND_fn_vFreeSnd(resource->field28);
        resource->field28 = 0;
        resource->type = 0;
        break;
    case 8:
        resource->type = 0;
        break;
    }
    QuitCriticalSectionTool();
}
