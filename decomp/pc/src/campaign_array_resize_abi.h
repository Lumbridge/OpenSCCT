#include "campaign_array_storage.h"

/* The stock allocator receives the heap in ECX and the buffer, byte count
   and allocation tag on the stack. The explicit second fastcall argument
   models the caller's scratch value in EDX at these virtual calls. */
struct ResizeHeap;
struct ResizeMethods {
    void *allocate;
    unsigned char *(__fastcall *resize)(struct ResizeHeap *heap, const void *edx_value,
                                        struct ArrayBuffer storage, int bytes, struct ArrayTag tag);
};
struct ResizeHeap { const struct ResizeMethods *methods; };
extern struct ResizeHeap *resize_heap;
