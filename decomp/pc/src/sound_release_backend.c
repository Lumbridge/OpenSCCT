void __stdcall EnterCriticalSectionTool_(void);
void __stdcall QuitCriticalSectionTool(void);
void __stdcall MouchardThreadTool(const char *source, unsigned long line);

typedef struct SoundResource {
    unsigned long id;
    unsigned long kind;
    unsigned char reserved[32];
    unsigned long group_data;
    unsigned char reserved2[16];
    unsigned long sample_data;
} SoundResource;
typedef struct ResourceArg { SoundResource *value; } ResourceArg;
typedef struct FlagsArg { unsigned long value; } FlagsArg;
struct SoundDriver;
typedef struct DriverMethods {
    void *reserved[25];
    void (__fastcall *unload)(struct SoundDriver *, ResourceArg resource, FlagsArg flags);
} DriverMethods;
typedef struct SoundDriver { const DriverMethods *methods; } SoundDriver;
#define DRIVER (*(SoundDriver **)0x1132ea00)
#define SOURCE_LABEL ((const char *)0x11175288)

void __cdecl sound_release_binary_backend(SoundResource *resource)
{
    if (resource) {
        EnterCriticalSectionTool_();
        MouchardThreadTool(SOURCE_LABEL, 0xde8);
        switch (resource->kind) {
        case 1: {
            ResourceArg block = { resource };
            FlagsArg flags = { 0 };
            EnterCriticalSectionTool_();
            MouchardThreadTool(SOURCE_LABEL, 0xe02);
            DRIVER->methods->unload(DRIVER, block, flags);
            resource->sample_data = 0;
            QuitCriticalSectionTool();
            break;
        }
        case 6:
            resource->group_data = 0;
            resource->kind = 0;
            break;
        case 8:
            resource->kind = 0;
            break;
        }
        QuitCriticalSectionTool();
    }
}
