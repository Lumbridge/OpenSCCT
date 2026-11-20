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


typedef struct Rotator { int pitch, yaw, roll; } Rotator;
typedef int (__cdecl *Rand)(void);
#define runtime_rand ((Rand const *)0x10bdf3ccu)

HANDLER(execRotRand, Rotator)
{
    int roll = 0;
    Rotator angles;
    Rand random;
    step(frame, &roll);
    ++frame->code;
    random = *runtime_rand;
    angles.yaw = (2 * random()) % 65535;
    angles.pitch = (2 * random()) % 65535;
    angles.roll = roll ? (2 * random()) % 65535 : 0;
    *result = angles;
}

HANDLER(execMultiply_RotatorFloat, Rotator)
{
    Rotator r;
    float scale;
    Rotator angles;
    step(frame, &r);
    scale = 0.0f;
    step(frame, &scale);
    ++frame->code;
    angles.pitch = (int)(r.pitch * scale);
    angles.yaw = (int)(r.yaw * scale);
    angles.roll = (int)(r.roll * scale);
    *result = angles;
}

HANDLER(execMultiply_FloatRotator, Rotator)
{
    float scale = 0.0f;
    Rotator r;
    Rotator angles;
    step(frame, &scale);
    step(frame, &r);
    ++frame->code;
    angles.pitch = (int)(r.pitch * scale);
    angles.yaw = (int)(r.yaw * scale);
    angles.roll = (int)(r.roll * scale);
    *result = angles;
}

HANDLER(execDivide_RotatorFloat, Rotator)
{
    Rotator r;
    float scale;
    Rotator angles;
    step(frame, &r);
    scale = 0.0f;
    step(frame, &scale);
    scale = *(const float *)0x10c109fcu / scale;
    ++frame->code;
    angles.pitch = (int)(r.pitch * scale);
    angles.yaw = (int)(r.yaw * scale);
    angles.roll = (int)(r.roll * scale);
    *result = angles;
}
