#ifndef CAMPAIGN_VM_PROPERTY_H
#define CAMPAIGN_VM_PROPERTY_H

#include "campaign_vm_frame.h"

extern void *campaign_property_address;

static __forceinline void *write_address(Frame *frame, void *fallback)
{
    void *address;
    campaign_property_address = 0;
    step(frame, fallback);
    address = campaign_property_address;
    return address ? address : fallback;
}

#endif
