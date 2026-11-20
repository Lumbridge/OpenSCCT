#include "campaign_array_growth.h"

static __forceinline void insert_range(struct Array *array, struct ArraySize index, struct ArraySize amount,
                                       struct ArraySize element, const unsigned short *allocation_tag)
{
    int first = extend_array(array, amount.value, element.value, allocation_tag);
    array_move(array->buffer.bytes + (index.value + amount.value) * element.value,
               array->buffer.bytes + index.value * element.value,
               (first - index.value) * element.value);
}

#define INSERT_RANGE(Entry, Tag) \
    void __fastcall insert_range_##Entry(struct Array *array, struct ArraySize index, struct ArraySize amount, struct ArraySize element) \
    { insert_range(array, index, amount, element, Tag); }

extern const unsigned short array_add_tag_7b7b08[];
extern const unsigned short array_add_tag_7ba13c[];
extern const unsigned short array_add_tag_7bc158[];
extern const unsigned short array_add_tag_7bc180[];
extern const unsigned short array_add_tag_7be9a8[];

INSERT_RANGE(r01f6d0, array_add_tag_7b7b08)
INSERT_RANGE(r0361f0, array_add_tag_7b7b08)
INSERT_RANGE(r036270, array_add_tag_7b7b08)
INSERT_RANGE(r048f60, array_add_tag_7bc158)
INSERT_RANGE(r048fe0, array_add_tag_7bc180)
INSERT_RANGE(r04bb30, array_add_tag_7b7b08)
INSERT_RANGE(r04bc00, array_add_tag_7b7b08)
INSERT_RANGE(r0807c0, array_add_tag_7b7b08)
INSERT_RANGE(r09de30, array_add_tag_7b7b08)
INSERT_RANGE(r12b750, array_add_tag_7be9a8)
INSERT_RANGE(r1ced90, array_add_tag_7b7b08)
INSERT_RANGE(r1d6540, array_add_tag_7b7b08)
INSERT_RANGE(r295640, array_add_tag_7b7b08)
INSERT_RANGE(r2da920, array_add_tag_7b7b08)
INSERT_RANGE(r2da9a0, array_add_tag_7b7b08)
INSERT_RANGE(r2daa20, array_add_tag_7b7b08)
INSERT_RANGE(r31ec30, array_add_tag_7b7b08)
INSERT_RANGE(r321bb0, array_add_tag_7ba13c)
INSERT_RANGE(r32bb70, array_add_tag_7b7b08)
INSERT_RANGE(r33e370, array_add_tag_7b7b08)
INSERT_RANGE(r357540, array_add_tag_7b7b08)
INSERT_RANGE(r411e80, array_add_tag_7b7b08)
INSERT_RANGE(r41a4c0, array_add_tag_7b7b08)
