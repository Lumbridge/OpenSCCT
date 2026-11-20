#ifndef CAMPAIGN_PRIMITIVE_CAST_H
#define CAMPAIGN_PRIMITIVE_CAST_H

#include "campaign_native_initializer.h"

extern void __fastcall campaign_undefined_native(ScriptObject *, unsigned, ScriptFrame *, void *);
extern CampaignNative campaign_cast_table[255];
extern int campaign_cast_initialized;
extern int campaign_cast_duplicate;

static __forceinline unsigned char campaign_assign_cast(int index, const CampaignNative *entry)
{
    if (!campaign_cast_initialized) {
        unsigned i;
        campaign_cast_initialized = 1;
        for (i = 0; i < 255; ++i)
            campaign_cast_table[i] = campaign_undefined_native;
    }
    if (index != -1) {
        if (index < 0 || (unsigned)index > 255 || campaign_cast_table[index] != campaign_undefined_native)
            campaign_cast_duplicate = index;
        campaign_cast_table[index] = *entry;
    }
    return 0;
}

#endif
