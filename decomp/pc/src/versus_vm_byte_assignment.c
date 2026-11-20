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
extern Native const native_table[];
#define DISPATCH native_table


extern void *property_address;
extern Object *property_owner;
extern void *property_descriptor;

static __forceinline void step(Frame *frame, void *result)
{
    Object *receiver = frame->object;
    unsigned int opcode = *frame->code++;
    DISPATCH[opcode](receiver, opcode, frame, result);
}

typedef void (__fastcall *OnPropertyWrite)(Object *, const void *, void *);
static __forceinline void *write_address(Frame *frame, void *fallback)
{
    Object *owner;
    void *address;
    Object *receiver = frame->object;
    unsigned int opcode = *frame->code++;
    property_address = 0;
    DISPATCH[opcode](receiver, opcode, frame, fallback);
    owner = property_owner;
    if (owner) {
        OnPropertyWrite *vtable = *(OnPropertyWrite **)owner;
        vtable[21](owner, vtable, property_descriptor);
    }
    address = property_address;
    return address ? address : fallback;
}

HANDLER(execMultiplyEqual_ByteByte, unsigned char)
{
    unsigned char fallback = 0;
    unsigned char *lhs = write_address(frame, &fallback);
    unsigned char rhs = 0;
    step(frame, &rhs);
    ++frame->code;
    *result = (*lhs *= rhs);
}

HANDLER(execDivideEqual_ByteByte, unsigned char)
{
    unsigned char fallback = 0;
    unsigned char *lhs = write_address(frame, &fallback);
    unsigned char rhs = 0;
    step(frame, &rhs);
    ++frame->code;
    *result = rhs ? (*lhs /= rhs) : 0;
}
