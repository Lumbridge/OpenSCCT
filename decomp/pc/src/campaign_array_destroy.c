#include "campaign_array_storage.h"

/* The stock x86 member call takes three integer words on the stack and an
   object pointer in ECX. VC7.1 fastcall passes the two constant words below
   on the stack when declared as float. The union supplies their integer bit
   patterns directly, including for element sizes above 127. */
union ArrayStackWord {
    int integer;
    float stack_argument;
};

#define DECLARE_REMOVE(Entry) \
    extern void __fastcall destroy_remove_##Entry(struct Array *, float first, \
                                                  struct ArraySize count, float stride)

#define DESTROY_ARRAY(Entry, Remove, ElementSize) \
    void __fastcall array_destroy_##Entry(struct Array *array) \
    { \
        static const union ArrayStackWord first = { 0 }; \
        static const union ArrayStackWord stride = { ElementSize }; \
        struct ArraySize count; \
        count.value = array->count; \
        destroy_remove_##Remove(array, first.stack_argument, count, stride.stack_argument); \
        if (array->buffer.bytes) { \
            struct ArrayBuffer storage; \
            storage.bytes = array->buffer.bytes; \
            array_allocator->methods->release(array_allocator, storage); \
        } \
        array->buffer.bytes = 0; \
        array->capacity = 0; \
        array->count = 0; \
    }

DECLARE_REMOVE(r019a00);
DECLARE_REMOVE(r029170);
DECLARE_REMOVE(r029260);
DECLARE_REMOVE(r029a00);
DECLARE_REMOVE(r029a90);
DECLARE_REMOVE(r029b20);
DECLARE_REMOVE(r029df0);
DECLARE_REMOVE(r036560);
DECLARE_REMOVE(r036600);
DECLARE_REMOVE(r036720);
DECLARE_REMOVE(r066210);
DECLARE_REMOVE(r0685d0);
DECLARE_REMOVE(r068660);
DECLARE_REMOVE(r0686f0);
DECLARE_REMOVE(r068780);
DECLARE_REMOVE(r071d10);
DECLARE_REMOVE(r077690);
DECLARE_REMOVE(r077960);
DECLARE_REMOVE(r091ae0);
DECLARE_REMOVE(r091c00);
DECLARE_REMOVE(r091ff0);
DECLARE_REMOVE(r0999b0);
DECLARE_REMOVE(r09df90);
DECLARE_REMOVE(r0a5e50);
DECLARE_REMOVE(r0ac340);
DECLARE_REMOVE(r0af390);
DECLARE_REMOVE(r0beb60);
DECLARE_REMOVE(r0d8470);
DECLARE_REMOVE(r10b9a0);
DECLARE_REMOVE(r124360);
DECLARE_REMOVE(r1293e0);
DECLARE_REMOVE(r129590);
DECLARE_REMOVE(r13ada0);
DECLARE_REMOVE(r146380);
DECLARE_REMOVE(r172cf0);
DECLARE_REMOVE(r1a8e50);
DECLARE_REMOVE(r2d7300);
DECLARE_REMOVE(r2dadc0);
DECLARE_REMOVE(r2daea0);
DECLARE_REMOVE(r2db4e0);
DECLARE_REMOVE(r2ec6b0);
DECLARE_REMOVE(r2f3270);
DECLARE_REMOVE(r2f77a0);
DECLARE_REMOVE(r2ffee0);
DECLARE_REMOVE(r3116b0);
DECLARE_REMOVE(r32e000);
DECLARE_REMOVE(r330f40);
DECLARE_REMOVE(r33a520);
DECLARE_REMOVE(r33ff30);
DECLARE_REMOVE(r347c60);
DECLARE_REMOVE(r34f1b0);
DECLARE_REMOVE(r34f840);
DECLARE_REMOVE(r355450);
DECLARE_REMOVE(r35bc00);

DESTROY_ARRAY(r029900, r029170, 4)
DESTROY_ARRAY(r029940, r029260, 12)
DESTROY_ARRAY(r02a6e0, r029170, 4)
DESTROY_ARRAY(r02a720, r019a00, 4)
DESTROY_ARRAY(r02a760, r029260, 12)
DESTROY_ARRAY(r02a960, r029a90, 4)
DESTROY_ARRAY(r02ab20, r029b20, 4)
DESTROY_ARRAY(r02ab60, r029a90, 4)
DESTROY_ARRAY(r02abe0, r029a00, 4)
DESTROY_ARRAY(r02ace0, r029df0, 4)
DESTROY_ARRAY(r03ac80, r036560, 4)
DESTROY_ARRAY(r03ad30, r036600, 4)
DESTROY_ARRAY(r03adb0, r036720, 80)
DESTROY_ARRAY(r0670c0, r066210, 1)
DESTROY_ARRAY(r069270, r0685d0, 44)
DESTROY_ARRAY(r0692b0, r068660, 4)
DESTROY_ARRAY(r0692f0, r0686f0, 8)
DESTROY_ARRAY(r069330, r068780, 40)
DESTROY_ARRAY(r071f50, r071d10, 416)
DESTROY_ARRAY(r0785c0, r077690, 4)
DESTROY_ARRAY(r0788e0, r077960, 4)
DESTROY_ARRAY(r0926b0, r091c00, 4)
DESTROY_ARRAY(r093400, r091ae0, 4)
DESTROY_ARRAY(r093500, r091ff0, 12)
DESTROY_ARRAY(r099b80, r0999b0, 8)
DESTROY_ARRAY(r09f1f0, r09df90, 4)
DESTROY_ARRAY(r0a5fb0, r0a5e50, 4)
DESTROY_ARRAY(r0ae280, r0ac340, 4)
DESTROY_ARRAY(r0af670, r0af390, 8)
DESTROY_ARRAY(r0c0be0, r0beb60, 4)
DESTROY_ARRAY(r0e2ff0, r0d8470, 48)
DESTROY_ARRAY(r10c020, r10b9a0, 4)
DESTROY_ARRAY(r124640, r124360, 32)
DESTROY_ARRAY(r129a50, r1293e0, 16)
DESTROY_ARRAY(r12a5d0, r129590, 20)
DESTROY_ARRAY(r13b440, r13ada0, 4)
DESTROY_ARRAY(r1487b0, r146380, 4)
DESTROY_ARRAY(r174e90, r172cf0, 20)
DESTROY_ARRAY(r1a9be0, r1a8e50, 176)
DESTROY_ARRAY(r2d7b80, r2d7300, 4)
DESTROY_ARRAY(r2dca60, r2dadc0, 4)
DESTROY_ARRAY(r2dcaa0, r2daea0, 8)
DESTROY_ARRAY(r2dcf10, r2db4e0, 4)
DESTROY_ARRAY(r2ee440, r2ec6b0, 4)
DESTROY_ARRAY(r2f3360, r2f3270, 12)
DESTROY_ARRAY(r2f7b80, r2f77a0, 8)
DESTROY_ARRAY(r3001b0, r2ffee0, 8)
DESTROY_ARRAY(r311860, r3116b0, 44)
DESTROY_ARRAY(r32e270, r32e000, 4)
DESTROY_ARRAY(r331f10, r330f40, 120)
DESTROY_ARRAY(r33a610, r33a520, 8)
DESTROY_ARRAY(r340d40, r33ff30, 4)
DESTROY_ARRAY(r347e50, r347c60, 24)
DESTROY_ARRAY(r34f420, r34f1b0, 8)
DESTROY_ARRAY(r34f9c0, r34f840, 4)
DESTROY_ARRAY(r355700, r355450, 12)
DESTROY_ARRAY(r35be00, r35bc00, 12)
