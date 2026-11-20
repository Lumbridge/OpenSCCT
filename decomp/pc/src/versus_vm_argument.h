/* The Steam native table contains x86 member handlers. Passing the decoded
 * opcode in the unused fastcall EDX position keeps both explicit VM
 * arguments on the stack, as they are in the retail calling sequence. */
struct ArgumentFrame {
    unsigned char unknown[8];
    void *context;
    const unsigned char *code;
};
typedef void (__fastcall *NativeHandler)(void *context, unsigned token,
                                         struct ArgumentFrame *frame, void *destination);
static __inline void read_argument(struct ArgumentFrame *frame, void *destination) {
    unsigned token = *frame->code++;
    ((NativeHandler *)0x10c819d8u)[token](frame->context, token, frame, destination);
}
