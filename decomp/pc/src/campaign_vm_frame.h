#ifndef CAMPAIGN_VM_FRAME_H
#define CAMPAIGN_VM_FRAME_H

typedef struct Frame Frame;
typedef struct Object Object;
typedef void (__fastcall *Native)(Object *, unsigned, Frame *, void *);
struct Frame {
    void *frame_prefix;
    void *node;
    Object *object;
    unsigned char *code;
};
typedef struct FrameArg { Frame *frame; } FrameArg;
/* The debug-info handler at campaign RVA 0x3f7780 ignores its second stack
   word. Passing x86 positive zero leaves EDX available and places the
   four-byte null result in that stack slot. */
typedef void (__fastcall *DebugInfoCall)(Object *, FrameArg, float);
extern Native campaign_native_table[];
extern Native campaign_cast_table[];
#define DISPATCH campaign_native_table
#define HANDLER(name, result_type) \
    void __fastcall name(Object *self, void *unused_edx, Frame *frame, result_type *result)

static __forceinline void step(Frame *frame, void *result)
{
    Object *receiver = frame->object;
    unsigned opcode = *frame->code++;
    DISPATCH[opcode](receiver, opcode, frame, result);
}

static __forceinline void finish_arguments(Frame *frame)
{
    ++frame->code;
    if (*frame->code == 0x42) {
        FrameArg call_frame = { frame };
        ++frame->code;
        ((DebugInfoCall)DISPATCH[0x42])(frame->object, call_frame, 0.0f);
    }
}

#endif
