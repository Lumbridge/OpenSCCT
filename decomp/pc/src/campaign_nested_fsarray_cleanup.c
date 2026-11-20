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



static __forceinline void clear_members(struct FSArray *records, int record_size,
                                         int member_offset, float stride)
{
    int i;
    for (i = 0; i < records->count; ++i) {
        unsigned char *record = records->buffer.bytes + i * record_size;
        clear_elements((struct FSArray *)(record + member_offset), stride);
    }
}

void __fastcall clear_records_0bf7f0(struct FSArray *records, struct ArraySize unused)
{
    static const union Stride stride = { 2 };
    clear_members(records, 36, 0, stride.stack_word);
    records->count = 0;
    if (records->buffer.bytes) {
        struct ArrayBuffer storage;
        storage.bytes = records->buffer.bytes;
        array_allocator->methods->release(array_allocator, storage);
    }
    records->buffer.bytes = 0;
}

void __fastcall clear_records_0e9330(struct FSArray *records, struct ArraySize unused)
{
    static const union Stride stride = { 4 };
    clear_members(records, 16, 0, stride.stack_word);
    records->count = 0;
    if (records->buffer.bytes) {
        struct ArrayBuffer storage;
        storage.bytes = records->buffer.bytes;
        array_allocator->methods->release(array_allocator, storage);
    }
    records->buffer.bytes = 0;
}
