#ifndef CAMPAIGN_NATIVE_INITIALIZER_H
#define CAMPAIGN_NATIVE_INITIALIZER_H

typedef struct ScriptFrame ScriptFrame;
typedef struct ScriptObject ScriptObject;

/* The receiver arrives in ECX. The VM supplies the frame and result on the stack. */
typedef void (__fastcall *CampaignNative)(ScriptObject *, unsigned, ScriptFrame *, void *);
unsigned char __cdecl campaign_register_native(int index, const CampaignNative *entry);

#define REGISTER_CAMPAIGN_NATIVE(name, index)                    \
    extern const CampaignNative callback_##name;                 \
    extern unsigned char registration_result_##name;             \
    void __cdecl register_##name(void)                           \
    {                                                            \
        registration_result_##name =                             \
            campaign_register_native((index), &callback_##name); \
    }

#endif
