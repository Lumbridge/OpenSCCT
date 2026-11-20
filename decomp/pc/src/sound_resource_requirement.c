typedef struct Sample Sample;
typedef struct { Sample *resource; unsigned fields[4]; } ChildResource;
struct Sample {
    unsigned id, type;
    char reserved[0x38];
    ChildResource *children;
    int count;
};

extern unsigned __cdecl locked_resource_channels(const Sample *);
extern unsigned __cdecl channel_requirement(unsigned channels);
extern void __stdcall EnterCriticalSectionTool_(void);
extern void __stdcall QuitCriticalSectionTool(void);
extern void __stdcall MouchardThreadTool(const char *site,int line);
extern const char MixerLockSite[];

#pragma code_seg(".text$V7596a0")
unsigned __cdecl resource_requirement(const Sample *sample)
{
    unsigned result;
    EnterCriticalSectionTool_();
    MouchardThreadTool(MixerLockSite, 0xbeb);
    result = 0;
    switch (sample->type) {
    case 1: case 6: case 9:
        result = channel_requirement(locked_resource_channels(sample));
        break;
    case 5: {
        int i;
        const ChildResource *child = sample->children;
        for (i = 0; i < sample->count; ++i, ++child) {
            unsigned requirement = resource_requirement(child->resource);
            if (result < requirement)
                result = requirement;
        }
        break;
    }
    default:
        QuitCriticalSectionTool();
        return 0;
    }
    QuitCriticalSectionTool();
    return result;
}
