#include "campaign_vm_property.h"

#define CHANGE_BY_ONE(name, type, expression) \
    HANDLER(name, type) \
    { \
        type fallback = 0; \
        type *value = write_address(frame, &fallback); \
        finish_arguments(frame); \
        *result = (expression); \
    }

CHANGE_BY_ONE(execAddAdd_PreByte, unsigned char, ++*value)
CHANGE_BY_ONE(execAddAdd_Byte, unsigned char, (*value)++)
CHANGE_BY_ONE(execSubtractSubtract_PreByte, unsigned char, --*value)
CHANGE_BY_ONE(execSubtractSubtract_Byte, unsigned char, (*value)--)
CHANGE_BY_ONE(execAddAdd_PreInt, int, ++*value)
CHANGE_BY_ONE(execAddAdd_Int, int, (*value)++)
CHANGE_BY_ONE(execSubtractSubtract_PreInt, int, --*value)
CHANGE_BY_ONE(execSubtractSubtract_Int, int, (*value)--)

#define ASSIGN(name, type, rhs_type, expression)                \
    HANDLER(name, type)                                         \
    {                                                           \
        type fallback = 0;                                      \
        type *lhs = write_address(frame, &fallback);            \
        rhs_type rhs = 0;                                       \
        step(frame, &rhs);                                      \
        finish_arguments(frame);                                \
        *result = (expression);                                 \
    }
ASSIGN(execAddEqual_ByteByte, unsigned char, unsigned char, *lhs += rhs)
ASSIGN(execSubtractEqual_ByteByte, unsigned char, unsigned char, *lhs -= rhs)
ASSIGN(execMultiplyEqual_ByteByte, unsigned char, unsigned char, *lhs *= rhs)
ASSIGN(execDivideEqual_ByteByte, unsigned char, unsigned char, rhs ? (*lhs /= rhs) : 0)
ASSIGN(execAddEqual_IntInt, int, int, *lhs += rhs)
ASSIGN(execSubtractEqual_IntInt, int, int, *lhs -= rhs)
ASSIGN(execMultiplyEqual_IntFloat, int, float, *lhs *= rhs)
ASSIGN(execAddEqual_FloatFloat, float, float, *lhs += rhs)
ASSIGN(execSubtractEqual_FloatFloat, float, float, *lhs -= rhs)
ASSIGN(execMultiplyEqual_FloatFloat, float, float, *lhs *= rhs)
