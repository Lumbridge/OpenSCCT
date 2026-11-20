typedef struct {
    unsigned char header[0x24];
    unsigned channels;
} Type1Sample;
typedef struct { unsigned unknown[2], channels, remaining[3]; } ChannelEntry;
typedef struct { unsigned char header[0x2c]; ChannelEntry *entries; int count; } Type6Sample;
typedef struct {
    int field, type;
    char gap[8];
    union {
        Type1Sample simple;
        Type6Sample collection;
        struct { char fields[0x3c]; unsigned channels; } other;
    } payload;
} Sample;
extern void __stdcall EnterCriticalSectionTool_(void);
extern void __stdcall MouchardThreadTool(const char *site,int line);
extern void __stdcall QuitCriticalSectionTool(void);
extern const char MixerLockSite[];

#pragma code_seg(".text$V751aa0")
__declspec(noinline) unsigned __cdecl simple_channels(const Type1Sample *sample)
{
    return sample->channels;
}
#pragma code_seg(".text$V757eb0")
__declspec(noinline) unsigned __cdecl collection_channels(const Type6Sample *sample)
{
    unsigned best = 0;
    int i;
    const ChannelEntry *entries = sample->entries;
    for (i = 0; i < sample->count; ++i)
        if (entries[i].channels > best)
            best = entries[i].channels;
    return best;
}
#pragma code_seg(".text$V759170")
__declspec(noinline) unsigned __cdecl locked_resource_channels(const Sample *sample)
{
    unsigned channels;
    EnterCriticalSectionTool_();
    MouchardThreadTool(MixerLockSite, 0xc0c);
    switch (sample->type) {
    case 1: channels = simple_channels(&sample->payload.simple); break;
    case 6: channels = collection_channels(&sample->payload.collection); break;
    case 9: channels = sample->payload.other.channels; break;
    default: QuitCriticalSectionTool(); return 0;
    }
    QuitCriticalSectionTool();
    return channels;
}
#pragma code_seg(".text$V74fe20")
__declspec(noinline) unsigned __cdecl query_resource_channels(const Sample *sample)
{
    return locked_resource_channels(sample);
}
#pragma code_seg(".text$V74a700")
unsigned __cdecl resource_channels(const Sample *sample)
{
    if (!sample)
        return 0;
    switch (sample->type) {
    case 1: case 6: case 9: return query_resource_channels(sample);
    default: return 0;
    }
}
