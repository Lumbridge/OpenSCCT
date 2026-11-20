#include "campaign_vm_frame.h"

HANDLER(execObjectConst, Object *)
{
    Object *object = *(Object *const *)frame->code;
    frame->code += sizeof(object);
    *result = object;
}

HANDLER(execNameConst, int)
{
    int name = *(const int *)frame->code;
    frame->code += sizeof(name);
    *result = name;
}

HANDLER(execTrue, int) { *result = 1; }
HANDLER(execFalse, int) { *result = 0; }
HANDLER(execNoObject, Object *) { *result = 0; }
