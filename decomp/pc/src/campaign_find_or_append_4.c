#include "campaign_fixed_record_growth.h"
struct ScalarRef { const unsigned int *value; };

#define FIND_OR_APPEND(Entry, Tag) \
    int __fastcall find_or_append_##Entry(struct Array *array, struct ScalarRef item) \
    { \
        int i; \
        for (i = 0; i < array->count; ++i) { \
            if (((unsigned int *)array->buffer.bytes)[i] == *item.value) return i; \
        } \
        i = extend_records(array, 1, sizeof(unsigned int), Tag); \
        ((unsigned int *)array->buffer.bytes)[i] = *item.value; \
        return i; \
    }

extern const unsigned short allocation_tag_7b7b08[];
extern const unsigned short allocation_tag_7ba17c[];
extern const unsigned short allocation_tag_7bc158[];

FIND_OR_APPEND(r04baa0, allocation_tag_7bc158)
FIND_OR_APPEND(r078660, allocation_tag_7b7b08)
FIND_OR_APPEND(r08c4f0, allocation_tag_7b7b08)
FIND_OR_APPEND(r093620, allocation_tag_7b7b08)
FIND_OR_APPEND(r09b9c0, allocation_tag_7b7b08)
FIND_OR_APPEND(r09c2c0, allocation_tag_7b7b08)
FIND_OR_APPEND(r0b5130, allocation_tag_7b7b08)
FIND_OR_APPEND(r12e040, allocation_tag_7b7b08)
FIND_OR_APPEND(r12e0b0, allocation_tag_7b7b08)
FIND_OR_APPEND(r132860, allocation_tag_7b7b08)
FIND_OR_APPEND(r1328d0, allocation_tag_7b7b08)
FIND_OR_APPEND(r140c50, allocation_tag_7b7b08)
FIND_OR_APPEND(r1aa100, allocation_tag_7b7b08)
FIND_OR_APPEND(r2b3500, allocation_tag_7b7b08)
FIND_OR_APPEND(r2cf080, allocation_tag_7ba17c)
FIND_OR_APPEND(r2dc700, allocation_tag_7b7b08)
FIND_OR_APPEND(r2df250, allocation_tag_7b7b08)
FIND_OR_APPEND(r314ad0, allocation_tag_7b7b08)
