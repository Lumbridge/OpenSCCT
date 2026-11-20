#include "campaign_vm_frame.h"

typedef struct Vector {float x,y,z;} Vector;
typedef struct Rotator {int pitch,yaw,roll;} Rotator;
typedef struct ScriptNode {
    unsigned char opaque[0x50];
    unsigned char *script;
} ScriptNode;
static __forceinline int read_int(Frame *frame)
{
    int value = *(const int *)frame->code;
    frame->code += sizeof(int);
    return value;
}

#define CAST_VALUE(name, source_type, destination_type, conversion) \
    HANDLER(name, destination_type) { \
        source_type value = 0; \
        step(frame, &value); \
        *result = (conversion); \
    }

HANDLER(execSelf, Object *)
{
    *result = self;
}

HANDLER(execStop, void)
{
    frame->code = 0;
}

HANDLER(execIntConst, int)
{
    *result = read_int(frame);
}

HANDLER(execFloatConst, float)
{
    float value = *(const float *)frame->code;
    frame->code += sizeof(float);
    *result = value;
}

HANDLER(execVectorConst, Vector)
{
    *result = *(const Vector *)frame->code;
    frame->code += sizeof(Vector);
}

HANDLER(execRotationConst, Rotator)
{
    result->pitch = read_int(frame);
    result->yaw = read_int(frame);
    result->roll = read_int(frame);
}

HANDLER(execByteConst, unsigned char)
{
    *result = *frame->code;
    ++frame->code;
}

HANDLER(execIntConstByte, int)
{
    *result = *frame->code;
    ++frame->code;
}

HANDLER(execIntZero, int)
{
    *result = 0;
}

HANDLER(execIntOne, int)
{
    *result = 1;
}

HANDLER(execPrimitiveCast, void)
{
    unsigned int cast = *frame->code++;
    campaign_cast_table[cast](frame->object, cast, frame, result);
}

CAST_VALUE(execByteToInt, unsigned char, int, (int)value)

CAST_VALUE(execByteToBool, unsigned char, int, value != 0)

CAST_VALUE(execByteToFloat, unsigned char, float, (float)value)

CAST_VALUE(execIntToByte, int, unsigned char, (unsigned char)value)
CAST_VALUE(execIntToBool, int, int, value != 0)
CAST_VALUE(execIntToFloat, int, float, (float)value)
CAST_VALUE(execBoolToByte, int, unsigned char, value & 1)
CAST_VALUE(execBoolToInt, int, int, value & 1)
CAST_VALUE(execBoolToFloat, int, float, (float)(value & 1))
CAST_VALUE(execFloatToByte, float, unsigned char, (unsigned char)value)
CAST_VALUE(execFloatToInt, float, int, (int)value)
CAST_VALUE(execFloatToBool, float, int, value != 0.0f)
CAST_VALUE(execObjectToBool, Object *, int, value != 0)
CAST_VALUE(execNameToBool, int, int, value != 0)

HANDLER(execJump, void)
{
    int offset = *(const unsigned short *)frame->code;
    frame->code += sizeof(unsigned short);
    frame->code = ((ScriptNode *)frame->node)->script + offset;
}
