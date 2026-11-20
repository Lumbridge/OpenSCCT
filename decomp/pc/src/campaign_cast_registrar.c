#include "campaign_primitive_cast.h"

unsigned char __cdecl campaign_register_cast(int index, const CampaignNative *entry)
{
    return campaign_assign_cast(index, entry);
}
