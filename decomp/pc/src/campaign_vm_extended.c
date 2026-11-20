#include "campaign_vm_frame.h"

#define HIGH_NATIVE(page)                                                   \
    HANDLER(execHighNative##page, void)                                     \
    {                                                                       \
        unsigned char low = *frame->code++;                                 \
        DISPATCH[(page) * 256 + low](self, low, frame, result);             \
    }

HIGH_NATIVE(0)
HIGH_NATIVE(1)
HIGH_NATIVE(2)
HIGH_NATIVE(3)
HIGH_NATIVE(4)
HIGH_NATIVE(5)
HIGH_NATIVE(6)
HIGH_NATIVE(7)
HIGH_NATIVE(8)
HIGH_NATIVE(9)
HIGH_NATIVE(10)
HIGH_NATIVE(11)
HIGH_NATIVE(12)
HIGH_NATIVE(13)
HIGH_NATIVE(14)
HIGH_NATIVE(15)
