typedef struct {
    unsigned id, type, gap08, active;
    unsigned gap10[4], owned, mode_or_format;
    void *collection_data;
    unsigned gap2c[3], external;
    void *sample_data;
} SoundResource;
typedef struct Driver Driver;
/* Aggregate arguments stay on the x86 stack while the fastcall receiver uses ECX. */
typedef struct { SoundResource *value; } SampleArgument;
typedef struct { int value; } ReleaseArgument;
typedef struct {
    void *reserved[25];
    void (__fastcall *release)(Driver *self, SampleArgument resource, ReleaseArgument full);
} VTable;
struct Driver { VTable *methods; };
extern Driver *SoundDriver;
extern void __stdcall SND_fn_vFreeSnd(void *ptr);
extern void __stdcall EnterCriticalSectionTool_(void);
extern void __stdcall QuitCriticalSectionTool(void);
extern void __stdcall MouchardThreadTool(const char *site, int line);
extern const char MixerLockSite[];

#pragma code_seg(".text$V758170")
void __cdecl release_sample(SoundResource *sample, int full)
{
    SampleArgument resource_arg;
    ReleaseArgument full_arg;
    EnterCriticalSectionTool_();
    MouchardThreadTool(MixerLockSite, 0xe02);
    resource_arg.value = sample;
    full_arg.value = full;
    SoundDriver->methods->release(SoundDriver, resource_arg, full_arg);
    if (full && sample->owned && !sample->external && sample->sample_data) {
        SND_fn_vFreeSnd(sample->sample_data);
        sample->sample_data = 0;
    }
    sample->sample_data = 0;
    QuitCriticalSectionTool();
}
#pragma code_seg(".text$V7581d0")
void __cdecl release_sample_collection(SoundResource *sample, int full)
{
    if (sample->active && full && !sample->mode_or_format && sample->collection_data)
        SND_fn_vFreeSnd(sample->collection_data);
    sample->collection_data = 0;
    sample->type = 0;
}
