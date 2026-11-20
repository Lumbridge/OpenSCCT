#include "campaign_vm_frame.h"

#define BINARY(name, operand_type, result_type, expression) \
    HANDLER(name, result_type) \
    { \
        operand_type a = 0, b; \
        step(frame, &a); \
        b = 0; \
        step(frame, &b); \
        finish_arguments(frame); \
        *result = (expression); \
    }
#define UNARY(name, operand_type, result_type, expression) \
    HANDLER(name, result_type) \
    { \
        operand_type a = 0; \
        step(frame, &a); \
        finish_arguments(frame); \
        *result = (expression); \
    }

double __cdecl campaign_sin(double);
double __cdecl campaign_cos(double);
double __cdecl campaign_sqrt(double);
double __cdecl campaign_atan(double);
double __cdecl campaign_tan(double);
double __cdecl campaign_exp(double);
double __cdecl campaign_loge(double);
double __cdecl campaign_asin(double);

BINARY(execMultiply_IntInt, int, int, a * b)

BINARY(execAdd_IntInt, int, int, a + b)

BINARY(execSubtract_IntInt, int, int, a - b)

BINARY(execLess_IntInt, int, int, a < b)

BINARY(execGreater_IntInt, int, int, a > b)

BINARY(execLessEqual_IntInt, int, int, a <= b)

BINARY(execGreaterEqual_IntInt, int, int, a >= b)

BINARY(execEqualEqual_IntInt, int, int, a == b)

BINARY(execNotEqual_IntInt, int, int, a != b)

BINARY(execAnd_IntInt, int, int, a & b)

BINARY(execOr_IntInt, int, int, a | b)

BINARY(execXor_IntInt, int, int, a ^ b)

BINARY(execLessLess_IntInt, int, int, a << b)

BINARY(execGreaterGreater_IntInt, int, int, a >> b)

BINARY(execDivide_IntInt, int, int, b ? a / b : 0)

BINARY(execGreaterGreaterGreater_IntInt, int, int, (unsigned)a >> b)

HANDLER(execMultiply_FloatFloat, float)
{
    float a = 0.0f;
    step(frame, &a);
    {
        float b = 0.0f;
        step(frame, &b);
        finish_arguments(frame);
        *result = a * b;
    }
}

HANDLER(execAdd_FloatFloat, float)
{
    float a = 0.0f;
    step(frame, &a);
    {
        float b = 0.0f;
        step(frame, &b);
        finish_arguments(frame);
        *result = a + b;
    }
}

HANDLER(execSubtract_FloatFloat, float)
{
    float a = 0.0f;
    step(frame, &a);
    {
        float b = 0.0f;
        step(frame, &b);
        finish_arguments(frame);
        *result = a - b;
    }
}

BINARY(execLess_FloatFloat, float, int, a < b)

BINARY(execGreater_FloatFloat, float, int, a > b)

BINARY(execLessEqual_FloatFloat, float, int, a <= b)

BINARY(execGreaterEqual_FloatFloat, float, int, a >= b)


BINARY(execMin, int, int, a <= b ? a : b)

BINARY(execMax, int, int, a >= b ? a : b)

BINARY(execEqualEqual_BoolBool, int, int, !a == !b)

BINARY(execNotEqual_BoolBool, int, int, !a != !b)

UNARY(execNot_PreBool, int, int, !a)

UNARY(execSubtract_PreInt, int, int, -a)

UNARY(execComplement_PreInt, int, int, ~a)

UNARY(execSin, float, float, (float)campaign_sin(a))
UNARY(execCos, float, float, (float)campaign_cos(a))
UNARY(execSqrt, float, float, (float)campaign_sqrt(a))
UNARY(execAtan, float, float, (float)campaign_atan(a))
UNARY(execTan, float, float, (float)campaign_tan(a))
UNARY(execExp, float, float, (float)campaign_exp(a))
UNARY(execLoge, float, float, (float)campaign_loge(a))
UNARY(execAsin, float, float, (float)campaign_asin(a))

UNARY(execSquare, float, float, a * a)
BINARY(execFMin,float,float,a <= b ? a : b)
BINARY(execFMax,float,float,a >= b ? a : b)
