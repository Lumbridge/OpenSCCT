#include "campaign_array_resize_abi.h"
struct ItemRef { const unsigned int *word; };

static __forceinline int remove_items(struct Array *array, struct ItemRef item, const unsigned short *allocation_tag)
{
    int original = array->count;
    int index;
    for (index = 0; index < array->count; ++index) {
        unsigned int *word = (unsigned int *)array->buffer.bytes + index;
        if (*word == *item.word) {
            array_move(word, word + 1, (array->count - index - 1) * sizeof(*word));
            --array->count;
            if ((3 * array->count < 2 * array->capacity ||
                 (array->capacity - array->count) * 4 >= 16384) &&
                (array->capacity - array->count > 64 || array->count == 0)) {
                struct ArrayTag tag;
                tag.text = allocation_tag;
                array->capacity = array->count;
                array->buffer.bytes = resize_heap->methods->resize(resize_heap, resize_heap->methods,
                    array->buffer, array->capacity * 4, tag);
            }
            --index;
        }
    }
    return original - array->count;
}

#define REMOVE_ITEMS(Entry, Tag) \
    int __fastcall Entry(struct Array *array, struct ItemRef item) \
    { return remove_items(array, item, Tag); }

extern const unsigned short allocation_tag_7b7b08[];
extern const unsigned short allocation_tag_7ba17c[];
extern const unsigned short allocation_tag_7bc158[];

REMOVE_ITEMS(remove_items_00cdc0, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_00ced0, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_0615c0, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_08c560, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_09b8b0, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_132460, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_132510, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_146820, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_15ff10, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_1dc520, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_20d270, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_2dc8e0, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_3258e0, allocation_tag_7bc158)
REMOVE_ITEMS(remove_items_3d9e20, allocation_tag_7ba17c)
REMOVE_ITEMS(remove_items_404650, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_413b10, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_413c20, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_413d30, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_413f10, allocation_tag_7b7b08)
REMOVE_ITEMS(remove_items_414020, allocation_tag_7b7b08)
