#include "campaign_fsarray.h"

/* Integer bits travel in stack slots for this x86 member call. */
extern void __fastcall remove_fsarray_range(struct FSArray *, float index,
                                            float amount, float stride);

static __forceinline void clear_elements(struct FSArray *array, float stride)
{

    union Stride amount;
    amount.bytes = array->count;
    remove_fsarray_range(array, 0.0f, amount.stack_word, stride);
    if (array->buffer.bytes) {
        struct ArrayBuffer storage;
        storage.bytes = array->buffer.bytes;
        array_allocator->methods->release(array_allocator, storage);
    }
    array->buffer.bytes = 0;
    array->count = 0;
}

#define CLEAR_ARRAY(Entry, ElementBytes) \
    void __fastcall Entry(struct FSArray *array) \
    { \
        static const union Stride stride = { ElementBytes }; \
        clear_elements(array, stride.stack_word); \
    }

#define DELETE_ARRAY(Entry, ElementBytes) \
    struct FSArray *__fastcall Entry(struct FSArray *array, struct ArraySize flags) \
    { \
        static const union Stride stride = { ElementBytes }; \
        clear_elements(array, stride.stack_word); \
        if (flags.value & 1) { \
            struct ArrayBuffer receiver; \
            receiver.bytes = (unsigned char *)array; \
            array_allocator->methods->release(array_allocator, receiver); \
        } \
        return array; \
    }




CLEAR_ARRAY(cleanup_080020, 1)
CLEAR_ARRAY(cleanup_081260, 4)
void __cdecl cleanup_0837a0(unsigned char *record)
{
    static const union Stride stride = { 4 };
    clear_elements((struct FSArray *)(record + 36), stride.stack_word);
}
CLEAR_ARRAY(cleanup_0b8260, 12)
CLEAR_ARRAY(cleanup_0b82d0, 2)
CLEAR_ARRAY(cleanup_0bef80, 2)
CLEAR_ARRAY(cleanup_0befd0, 2)
CLEAR_ARRAY(cleanup_0bf0c0, 4)
DELETE_ARRAY(cleanup_0bf220, 2)
DELETE_ARRAY(cleanup_0bf270, 2)
CLEAR_ARRAY(cleanup_0d7940, 6)
CLEAR_ARRAY(cleanup_0dcbc0, 8)
CLEAR_ARRAY(cleanup_0df3a0, 4)
CLEAR_ARRAY(cleanup_0df3e0, 128)
CLEAR_ARRAY(cleanup_0df420, 60)
CLEAR_ARRAY(cleanup_0df460, 156)
CLEAR_ARRAY(cleanup_0df4a0, 96)
CLEAR_ARRAY(cleanup_0df9c0, 4)
DELETE_ARRAY(cleanup_0e4d00, 4)
CLEAR_ARRAY(cleanup_1e0c00, 4)
CLEAR_ARRAY(cleanup_3da460, 4)
CLEAR_ARRAY(cleanup_3e5430, 12)
