#include "campaign_vm_frame.h"
typedef struct Rotator { int pitch, yaw, roll; } Rotator;
HANDLER(execAdd_RotatorRotator, Rotator)
{
    Rotator a, b, value;
    step(frame, &a);
    step(frame, &b);
    finish_arguments(frame);
    value.pitch = a.pitch + b.pitch;
    value.yaw = a.yaw + b.yaw;
    value.roll = a.roll + b.roll;
    *result = value;
}

HANDLER(execSubtract_RotatorRotator, Rotator)
{
    Rotator a, b, value;
    step(frame, &a);
    step(frame, &b);
    finish_arguments(frame);
    value.pitch = a.pitch - b.pitch;
    value.yaw = a.yaw - b.yaw;
    value.roll = a.roll - b.roll;
    *result = value;
}

HANDLER(execEqualEqual_RotatorRotator, int)
{
    Rotator a, b;
    step(frame, &a);
    step(frame, &b);
    finish_arguments(frame);
    *result = a.pitch == b.pitch && a.yaw == b.yaw && a.roll == b.roll;
}

HANDLER(execNotEqual_RotatorRotator, int)
{
    Rotator a, b;
    step(frame, &a);
    step(frame, &b);
    finish_arguments(frame);
    *result = a.pitch != b.pitch || a.yaw != b.yaw || a.roll != b.roll;
}

HANDLER(execClamp, int)
{
    int value = 0, low, high;
    step(frame, &value);
    low = 0;
    step(frame, &low);
    high = 0;
    step(frame, &high);
    finish_arguments(frame);
    *result = value < low ? low : (value < high ? value : high);
}

HANDLER(execLerp, float)
{
    float fraction = 0, start, end;
    step(frame, &fraction);
    start = 0;
    step(frame, &start);
    end = 0;
    step(frame, &end);
    finish_arguments(frame);
    *result = start + fraction * (end - start);
}
