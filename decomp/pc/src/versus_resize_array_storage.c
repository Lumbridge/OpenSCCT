#include "campaign_array_resize_abi.h"

static __forceinline void resize_storage(struct Array *array, struct ArraySize element,
                                         const unsigned short *allocation_tag)
{
    struct ArrayTag tag;
    tag.text = allocation_tag;
    array->buffer.bytes = resize_heap->methods->resize(
        resize_heap, array->buffer.bytes, array->buffer, array->capacity * element.value, tag);
}

#define RESIZE_ARRAY(Entry, Tag) \
    void __fastcall resize_array_##Entry(struct Array *array, struct ArraySize element) \
    { resize_storage(array, element, Tag); }

extern const unsigned short array_tag[];

RESIZE_ARRAY(r004600, array_tag)
