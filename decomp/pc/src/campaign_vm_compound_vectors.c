#include "campaign_vm_property.h"

typedef struct VMVector { float x, y, z; } VMVector;
typedef struct VMRotator {int pitch, yaw, roll;} VMRotator;

#define READ_PROPERTY(type, name)                                        \
    static __forceinline type *name(Frame *frame, type *fallback)        \
    {                                                                    \
        type *value;                                                     \
        campaign_property_address = 0;                                   \
        step(frame, fallback);                                           \
        value = (type *)campaign_property_address;                       \
        if (!value)                                                      \
            value = fallback;                                           \
        return value;                                                    \
    }

READ_PROPERTY(VMVector, vector_property)
READ_PROPERTY(VMRotator, rotator_property)

HANDLER(execMultiplyEqual_VectorVector, VMVector)
{
    VMVector fallback;
    VMVector *lhs = vector_property(frame, &fallback);
    VMVector rhs;
    step(frame, &rhs);
    finish_arguments(frame);
    lhs->x *= rhs.x;
    lhs->y *= rhs.y;
    lhs->z *= rhs.z;
    *result = *lhs;
}

HANDLER(execAddEqual_VectorVector, VMVector)
{
    VMVector fallback;
    VMVector *lhs = vector_property(frame, &fallback);
    VMVector rhs;
    step(frame, &rhs);
    finish_arguments(frame);
    lhs->x += rhs.x;
    lhs->y += rhs.y;
    lhs->z += rhs.z;
    *result = *lhs;
}

HANDLER(execSubtractEqual_VectorVector, VMVector)
{
    VMVector fallback;
    VMVector *lhs = vector_property(frame, &fallback);
    VMVector rhs;
    step(frame, &rhs);
    finish_arguments(frame);
    lhs->x -= rhs.x;
    lhs->y -= rhs.y;
    lhs->z -= rhs.z;
    *result = *lhs;
}

HANDLER(execMultiplyEqual_VectorFloat, VMVector)
{
    VMVector fallback;
    VMVector *lhs = vector_property(frame, &fallback);
    float rhs = 0.0f, factor;
    step(frame, &rhs);
    finish_arguments(frame);
    factor = rhs;
    lhs->x *= factor;
    lhs->y *= factor;
    lhs->z *= factor;
    *result = *lhs;
}

HANDLER(execAddEqual_RotatorRotator, VMRotator)
{
    VMRotator fallback;
    VMRotator *lhs = rotator_property(frame, &fallback);
    VMRotator rhs;
    step(frame, &rhs);
    finish_arguments(frame);
    lhs->pitch += rhs.pitch;
    lhs->yaw += rhs.yaw;
    lhs->roll += rhs.roll;
    *result = *lhs;
}

HANDLER(execSubtractEqual_RotatorRotator, VMRotator)
{
    VMRotator fallback;
    VMRotator *lhs = rotator_property(frame, &fallback);
    VMRotator rhs;
    step(frame, &rhs);
    finish_arguments(frame);
    lhs->pitch -= rhs.pitch;
    lhs->yaw -= rhs.yaw;
    lhs->roll -= rhs.roll;
    *result = *lhs;
}

HANDLER(execMultiplyEqual_RotatorFloat, VMRotator)
{
    VMRotator fallback;
    VMRotator *lhs = rotator_property(frame, &fallback);
    float rhs = 0.0f, factor;
    step(frame, &rhs);
    finish_arguments(frame);
    factor = rhs;
    lhs->pitch *= factor;
    lhs->yaw *= factor;
    lhs->roll *= factor;
    *result = *lhs;
}
