#include "campaign_array_storage.h"

static __forceinline int extend_array(struct Array *array, int amount, int element_size,
                                      const unsigned short *allocation_tag)
{
    int old_count = array->count;
    array->count += amount;
    if (array->count > array->capacity) {
        struct ArraySize bytes;
        struct ArrayTag tag;
        array->capacity = array->count + (3 * array->count) / 8 + 2;
        bytes.value = array->capacity * element_size;
        tag.text = allocation_tag;
        array->buffer.bytes = array_allocator->methods->resize(array_allocator, array->buffer, bytes, tag);
    }
    return old_count;
}
