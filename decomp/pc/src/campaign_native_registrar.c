#include "campaign_native_initializer.h"

extern void __fastcall campaign_undefined_native(ScriptObject *, unsigned, ScriptFrame *, void *);
extern CampaignNative campaign_native_table[4096];
extern int campaign_native_initialized;
extern int campaign_native_duplicate;

unsigned char __cdecl campaign_register_native(int index, const CampaignNative *entry)
{
    if (!campaign_native_initialized) {
        unsigned i;
        campaign_native_initialized = 1;
        if (sizeof(*entry) != sizeof(void *))
            goto register_index;
        for (i = 0; i < 4096; ++i)
            campaign_native_table[i] = campaign_undefined_native;
    }
register_index:
    if (index != -1) {
        if (index < 0 || (unsigned)index > 4096 || campaign_native_table[index] != campaign_undefined_native)
            campaign_native_duplicate = index;
        campaign_native_table[index] = *entry;
    }
    return 0;
}
