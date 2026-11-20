#include "versus_vm_argument.h"

typedef struct Rotator { int pitch, yaw, roll; } Rotator;
typedef int (__cdecl *Rand)(void);
#define runtime_rand ((Rand const *)0x10bdf3ccu)

void __fastcall execRotRandWithAngle(void *self, unsigned int unused_edx, struct ArgumentFrame *frame, Rotator *result)
{
    int range = 0;
    Rotator angles;
    Rand random;
    read_argument(frame, &range);
    ++frame->code;
    random = *runtime_rand;
    angles.yaw = (int)((2 * random()) % range - range * *(const float *)0x10c10a10u);
    angles.pitch = (int)((2 * random()) % range - range * *(const float *)0x10c10a10u);
    angles.roll = (int)((2 * random()) % range - range * *(const float *)0x10c10a10u);
    *result = angles;
}
