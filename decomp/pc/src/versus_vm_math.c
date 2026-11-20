/* The x86 native handler ABI passes the receiver in ecx, followed by
   the script frame and result pointer on the stack. */
typedef struct Frame Frame;
typedef struct Object Object;
typedef void (__fastcall *Native)(Object *, unsigned int, Frame *, void *);
struct Frame {
    void *frame_prefix[2];
    Object *object;
    unsigned char *code;
};
#define HANDLER(name, result_type) \
    void __fastcall name(Object *self, void *unused_edx, Frame *frame, result_type *result)
/* Steam 252084 initializes its UnrealScript native table at this image VA. */
#define DISPATCH ((Native const *)0x10c819d8u)

static __forceinline void step(Frame *frame, void *result)
{
    Object *receiver = frame->object;
    unsigned int opcode = *frame->code++;
    DISPATCH[opcode](receiver, opcode, frame, result);
}


#define BINARY(name, operand_type, result_type, expression) \
    HANDLER(name, result_type) \
    { \
        operand_type a = 0, b; \
        step(frame, &a); \
        b = 0; \
        step(frame, &b); \
        ++frame->code; \
        *result = (expression); \
    }
#define UNARY(name, operand_type, result_type, expression) \
    HANDLER(name, result_type) \
    { \
        operand_type a = 0; \
        step(frame, &a); \
        ++frame->code; \
        *result = (expression); \
    }

double __cdecl sin(double), cos(double), sqrt(double);

typedef int (__cdecl *Rand)(void);
typedef void (__fastcall *KillEmitter)(Object *, void *);
typedef void (__fastcall *CrouchPawn)(Object *, void *, int);

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
        ++frame->code;
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
        ++frame->code;
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
        ++frame->code;
        *result = a - b;
    }
}

BINARY(execLess_FloatFloat, float, int, a < b)

BINARY(execGreater_FloatFloat, float, int, a > b)

BINARY(execLessEqual_FloatFloat, float, int, a <= b)

BINARY(execGreaterEqual_FloatFloat, float, int, a >= b)

BINARY(execDivide_FloatFloat, float, float, a / b)

BINARY(execMin, int, int, a <= b ? a : b)

BINARY(execMax, int, int, a >= b ? a : b)

BINARY(execEqualEqual_BoolBool, int, int, !a == !b)

BINARY(execNotEqual_BoolBool, int, int, !a != !b)

UNARY(execNot_PreBool, int, int, !a)

UNARY(execSubtract_PreInt, int, int, -a)

UNARY(execComplement_PreInt, int, int, ~a)

UNARY(execSin, float, float, (float)sin(a))

UNARY(execCos, float, float, (float)cos(a))

UNARY(execSqrt, float, float, (float)sqrt(a))

HANDLER(execFRand, float)
{
    const Rand *crt_rand = (const Rand *)0x10bdf3ccu;
    const float *inverse_range = (const float *)0x10c10a88u;
    ++frame->code;
    *result = (*crt_rand)() * *inverse_range;
}

HANDLER(execKill, void)
{
    KillEmitter *vtable;
    ++frame->code;
    vtable = *(KillEmitter **)self;
    vtable[80](self, unused_edx);
}

HANDLER(execForceCrouch, void)
{
    CrouchPawn *vtable;
    ++frame->code;
    vtable = *(CrouchPawn **)self;
    vtable[81](self, unused_edx, 0);
}
