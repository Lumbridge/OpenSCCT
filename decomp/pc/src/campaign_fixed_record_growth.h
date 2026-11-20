#include "campaign_array_resize_abi.h"

/* The original element types are unknown at these entries. Only their byte
   width and the operation performed on each record are used here. */
static __forceinline int extend_records(struct Array *array, int amount, unsigned int record_size,
                                        const unsigned short *tag_text)
{
    int index = array->count;
    array->count += amount;
    if (array->count > array->capacity) {
        struct ArrayTag tag;
        array->capacity = array->count + (3 * array->count) / 8 + 2;
        tag.text = tag_text;
        array->buffer.bytes = resize_heap->methods->resize(
            resize_heap, resize_heap->methods, array->buffer, array->capacity * record_size, tag);
    }
    return index;
}
