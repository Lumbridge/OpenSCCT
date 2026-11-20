#include "campaign_array_resize_abi.h"

struct ArrayItemRef { const unsigned int *value; };

static __forceinline int append_item(struct Array *array, struct ArrayItemRef item,
                                     const unsigned short *allocation_tag)
{
    int index = array->count;
    if (++array->count > array->capacity) {
        struct ArrayTag tag;
        array->capacity = array->count + (3 * array->count) / 8 + 2;
        tag.text = allocation_tag;
        array->buffer.bytes = resize_heap->methods->resize(
            resize_heap, resize_heap->methods, array->buffer, array->capacity * sizeof(unsigned int), tag);
    }
    ((unsigned int *)array->buffer.bytes)[index] = *item.value;
    return index;
}

#define APPEND_ARRAY_ITEM(Entry, Tag) \
    int __fastcall append_array_item_##Entry(struct Array *array, struct ArrayItemRef item) \
    { return append_item(array, item, Tag); }

extern const unsigned short heap_tag_7b7b08[];
extern const unsigned short heap_tag_7ba13c[];
extern const unsigned short heap_tag_7ba17c[];
extern const unsigned short heap_tag_7bc158[];
extern const unsigned short heap_tag_7db4cc[];
extern const unsigned short heap_tag_831360[];

APPEND_ARRAY_ITEM(r00cd60, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r00ce70, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r00cff0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r036000, heap_tag_7bc158)
APPEND_ARRAY_ITEM(r03aa40, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r03aaa0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r03ab00, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r03acc0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r0614c0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r061560, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r0616d0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r061770, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r078600, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r078920, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r088360, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r0883c0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r092900, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r09b960, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r09e5a0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r0b50d0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r0c0740, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r0c0c60, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r0d8f60, heap_tag_7ba13c)
APPEND_ARRAY_ITEM(r0e8ae0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r0e9a30, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r12da50, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r12dab0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1325c0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r132620, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1385b0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r13f190, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r146760, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1467c0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r15f230, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r15fc60, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r15feb0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r189fa0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r197750, heap_tag_7ba17c)
APPEND_ARRAY_ITEM(r1a9c20, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1af2d0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1af490, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1b6ca0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1cf020, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1dc420, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1dc4c0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1e15e0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1e1680, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1e2840, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1e3790, heap_tag_7db4cc)
APPEND_ARRAY_ITEM(r1e3830, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1e88e0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1f6d50, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r1f8a20, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r20d210, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2364b0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r236550, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r23b010, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r247ec0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r26e9a0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r26f680, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2761c0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r279dc0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2978b0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r297980, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2b1fe0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2cf0f0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2dc6a0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2dca00, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2dcc30, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2e61c0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2e6220, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r2fe9e0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r313600, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r313720, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r313780, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r31def0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r324840, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r32c300, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r32e1b0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r34a7d0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r3d4650, heap_tag_831360)
APPEND_ARRAY_ITEM(r4016c0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r4045f0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r40bde0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r40be40, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r40d770, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r413a50, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r413ab0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r413bc0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r413cd0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r413de0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r413eb0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r413fc0, heap_tag_7b7b08)
APPEND_ARRAY_ITEM(r41af80, heap_tag_7b7b08)
