#include "campaign_array_growth.h"

struct GrowthRequest { int amount; int element_size; };

extern const unsigned short tag_7b7b08[];

#define GROW_ARRAY(Entry, Tag) \
    int __fastcall Entry(struct Array *array, struct GrowthRequest request) \
    { \
        return extend_array(array, request.amount, request.element_size, Tag); \
    }

GROW_ARRAY(grow_00d4a0, tag_7b7b08)
GROW_ARRAY(grow_00d4f0, tag_7b7b08)
GROW_ARRAY(grow_00d9b0, tag_7b7b08)
GROW_ARRAY(grow_015b20, tag_7b7b08)
GROW_ARRAY(grow_019190, tag_7b7b08)
GROW_ARRAY(grow_019b80, tag_7b7b08)
GROW_ARRAY(grow_01f2d0, tag_7b7b08)
GROW_ARRAY(grow_01f750, tag_7b7b08)
GROW_ARRAY(grow_023570, tag_7b7b08)
GROW_ARRAY(grow_0238e0, tag_7b7b08)
GROW_ARRAY(grow_023d90, tag_7b7b08)
GROW_ARRAY(grow_069590, tag_7b7b08)
GROW_ARRAY(grow_069820, tag_7b7b08)
GROW_ARRAY(grow_069a50, tag_7b7b08)
GROW_ARRAY(grow_069d10, tag_7b7b08)
GROW_ARRAY(grow_06f220, tag_7b7b08)
GROW_ARRAY(grow_072170, tag_7b7b08)
GROW_ARRAY(grow_07bba0, tag_7b7b08)
GROW_ARRAY(grow_07bcd0, tag_7b7b08)
GROW_ARRAY(grow_0c4940, tag_7b7b08)
GROW_ARRAY(grow_3d05b0, tag_7b7b08)
