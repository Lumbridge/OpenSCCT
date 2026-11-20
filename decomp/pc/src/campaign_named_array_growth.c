#include "campaign_array_growth.h"

struct GrowthRequest { int amount; int element_size; };

extern const unsigned short tag_7b8f84[];
extern const unsigned short tag_7b9b70[];
extern const unsigned short tag_7ba13c[];
extern const unsigned short tag_7ba17c[];
extern const unsigned short tag_7be9a8[];

#define GROW_ARRAY(Entry, Tag) \
    int __fastcall Entry(struct Array *array, struct GrowthRequest request) \
    { \
        return extend_array(array, request.amount, request.element_size, Tag); \
    }

GROW_ARRAY(grow_011e10, tag_7b8f84)
GROW_ARRAY(grow_015560, tag_7b9b70)
GROW_ARRAY(grow_017460, tag_7ba13c)
GROW_ARRAY(grow_0176d0, tag_7ba17c)
GROW_ARRAY(grow_066c50, tag_7be9a8)
