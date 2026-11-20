#include "campaign_vm_frame.h"

typedef struct VMVector { float x, y, z; } VMVector;

#define VECTOR_BINARY(name, operation)                                      \
    HANDLER(name, VMVector)                                                 \
    {                                                                       \
        VMVector a, b, vector;                                              \
        step(frame, &a);                                                    \
        step(frame, &b);                                                    \
        finish_arguments(frame);                                            \
        vector.x = a.x operation b.x;                                       \
        vector.y = a.y operation b.y;                                       \
        vector.z = a.z operation b.z;                                       \
        *result = vector;                                                   \
    }

VECTOR_BINARY(execMultiply_VectorVector, *)
VECTOR_BINARY(execAdd_VectorVector, +)
VECTOR_BINARY(execSubtract_VectorVector, -)

HANDLER(execDot_VectorVector, float)
{
    VMVector a, b;
    step(frame, &a);
    step(frame, &b);
    finish_arguments(frame);
    *result = a.x * b.x + a.y * b.y + a.z * b.z;
}

HANDLER(execSubtract_PreVector, VMVector)
{
    VMVector input, value;
    step(frame, &input);
    finish_arguments(frame);
    value.x = -input.x;
    value.y = -input.y;
    value.z = -input.z;
    *result = value;
}

double __cdecl campaign_sqrt(double);

HANDLER(execVSize, float)
{
    VMVector v;
    step(frame, &v);
    finish_arguments(frame);
    *result = (float)campaign_sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
}

HANDLER(execVSize2D, float)
{
    VMVector v;
    step(frame, &v);
    finish_arguments(frame);
    *result = (float)campaign_sqrt(v.x * v.x + v.y * v.y);
}

#define VECTOR_SCALE(name, first, second)                             \
    HANDLER(name, VMVector)                                            \
    {                                                                  \
        VMVector vector, scaled;                                       \
        float factor;                                                  \
        first;                                                         \
        second;                                                        \
        finish_arguments(frame);                                       \
        scaled.x = vector.x * factor;                                  \
        scaled.y = vector.y * factor;                                  \
        scaled.z = vector.z * factor;                                  \
        *result = scaled;                                              \
    }

VECTOR_SCALE(execMultiply_VectorFloat, step(frame, &vector), (factor = 0, step(frame, &factor)))
VECTOR_SCALE(execMultiply_FloatVector, (factor = 0, step(frame, &factor)), step(frame, &vector))

#define VECTOR_COMPARE(name, condition)                               \
    HANDLER(name, int)                                                 \
    {                                                                  \
        VMVector a, b;                                                 \
        step(frame, &a);                                               \
        step(frame, &b);                                               \
        finish_arguments(frame);                                       \
        *result = (condition);                                         \
    }

VECTOR_COMPARE(execEqualEqual_VectorVector, a.x == b.x && a.y == b.y && a.z == b.z)
VECTOR_COMPARE(execNotEqual_VectorVector, a.x != b.x || a.y != b.y || a.z != b.z)
