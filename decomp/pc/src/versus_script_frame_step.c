struct Object;
struct Frame { void *reserved[3]; const unsigned char *code; };
struct FrameArg { struct Frame *ptr; };
struct Result { void *ptr; };
typedef void (__fastcall *Native)(struct Object *, unsigned, struct Frame *, void *);
extern const Native native_table[];
void __fastcall step(struct Object *self, struct FrameArg arg, struct Result result)
{
    struct Frame *frame = arg.ptr;
    unsigned char opcode = *frame->code;
    ++frame->code;
    native_table[opcode](self, opcode, frame, result.ptr);
}
