#include "campaign_fsarray.h"

extern const unsigned short fsarray_tag[];

void __fastcall remove_fsarray_range(struct FSArray *array, struct ArraySize index,
                                     struct ArraySize amount, struct ArraySize stride)
{
    struct ArrayTag tag;
    struct ArraySize bytes;
    if (amount.value) {
        array_move(array->buffer.bytes + index.value * stride.value,
                   array->buffer.bytes + (index.value + amount.value) * stride.value,
                   (array->count - index.value - amount.value) * stride.value);
        array->count -= amount.value;
        tag.text = fsarray_tag;
        bytes.value = array->count * stride.value;
        array->buffer.bytes = array_allocator->methods->resize(
            array_allocator, array->buffer, bytes, tag);
    }
}
