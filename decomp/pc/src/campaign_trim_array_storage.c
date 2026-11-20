#include "campaign_array_resize_abi.h"

extern const unsigned short tag_7b78d8[];
extern const unsigned short tag_7b7b08[];
extern const unsigned short tag_7bc158[];
extern const unsigned short tag_7bc180[];

static __forceinline void trim(struct Array *array, int width, const unsigned short *label)
{
    if (array->capacity != array->count) {
        struct ArrayTag tag;
        tag.text = label;
        array->capacity = array->count;
        array->buffer.bytes = resize_heap->methods->resize(
            resize_heap, resize_heap->methods, array->buffer, array->capacity * width, tag);
    }
}

#define TRIM_ARRAY(Entry, Tag) \
    void __fastcall Entry(struct Array *array, struct ArraySize width) \
    { trim(array, width.value, Tag); }

TRIM_ARRAY(trim_07fda0, tag_7bc158)
TRIM_ARRAY(trim_07fdd0, tag_7bc180)
TRIM_ARRAY(trim_0806d0, tag_7b7b08)
TRIM_ARRAY(trim_080700, tag_7b7b08)
TRIM_ARRAY(trim_080730, tag_7b7b08)
TRIM_ARRAY(trim_080760, tag_7b7b08)
TRIM_ARRAY(trim_080790, tag_7b7b08)
TRIM_ARRAY(trim_0b6d10, tag_7b78d8)
TRIM_ARRAY(trim_0be950, tag_7b7b08)
TRIM_ARRAY(trim_0bf120, tag_7b7b08)
TRIM_ARRAY(trim_189c40, tag_7b7b08)
