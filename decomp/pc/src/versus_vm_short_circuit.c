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
#define DISPATCH ((Native const *)0x10c819d8u)


static __forceinline void step(Frame *frame, void *result)
{
    Object *receiver = frame->object;
    unsigned int opcode = *frame->code++;
    DISPATCH[opcode](receiver, opcode, frame, result);
}


/* Scalar functions expanded to x87 CRT intrinsic helpers by the period compiler. */
double __cdecl pow(double, double), fmod(double, double), asin(double), acos(double);

HANDLER(execAndAnd_BoolBool, int)
{
    int a = 0;
    unsigned short skip;
    step(frame, &a);
    ++frame->code;
    skip = *(const unsigned short *)frame->code;
    frame->code += sizeof(skip);
    if (a) {
        int b = 0;
        step(frame, &b);
        *result = a && b;
        ++frame->code;
    } else {
        *result = 0;
        frame->code += skip;
    }
}

HANDLER(execOrOr_BoolBool, int)
{
    int a = 0;
    unsigned short skip;
    step(frame, &a);
    ++frame->code;
    skip = *(const unsigned short *)frame->code;
    frame->code += sizeof(skip);
    if (!a) {
        int b = 0;
        step(frame, &b);
        *result = a || b;
        ++frame->code;
    } else {
        *result = 1;
        frame->code += skip;
    }
}

HANDLER(execXorXor_BoolBool, int)
{
    int a = 0, b;
    step(frame, &a);
    b = 0;
    step(frame, &b);
    ++frame->code;
    *result = (!a) ^ (!b);
}

HANDLER(execMultiplyMultiply_FloatFloat, float)
{
    float a = 0.0f;
    float b;
    step(frame, &a);
    b = 0.0f;
    step(frame, &b);
    ++frame->code;
    *result = (float)pow(a, b);
}

HANDLER(execPercent_FloatFloat, float)
{
    float a = 0.0f;
    float b;
    step(frame, &a);
    b = 0.0f;
    step(frame, &b);
    ++frame->code;
    *result = (float)fmod(a, b);
}

HANDLER(execAsin, float)
{
    float a = 0.0f;
    step(frame, &a);
    ++frame->code;
    *result = (float)asin(a);
}

HANDLER(execAcos, float)
{
    float a = 0.0f;
    step(frame, &a);
    ++frame->code;
    *result = (float)acos(a);
}

HANDLER(execComplementEqual_FloatFloat, int)
{
    float a = 0.0f;
    float b;
    float difference;
    step(frame, &a);
    b = 0.0f;
    step(frame, &b);
    ++frame->code;
    difference = a - b;
    *result = (difference >= *(const float *)0x10c109f8u ? difference : -difference) < *(const double *)0x10c109f0u;
}
