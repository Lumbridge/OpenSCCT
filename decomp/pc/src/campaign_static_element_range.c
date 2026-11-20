#include "campaign_array_storage.h"

#define CLEANUP(Entry, Object, Remove) \
void Entry(void) \
{ \
    struct ArraySize count; \
    count.value = Object.count; \
    Remove(&Object, 0.0f, count); \
    release_storage(&Object); \
}

extern struct Array static_element_range_93282c;
extern struct Array static_element_range_a120c0;
extern struct Array static_element_range_a120cc;
extern void __fastcall remove_range_013560(struct Array *, float start, struct ArraySize count);
extern void __fastcall remove_range_01d320(struct Array *, float start, struct ArraySize count);
extern void __fastcall remove_range_3ca9d0(struct Array *, float start, struct ArraySize count);

CLEANUP(cleanup_element_range_7b35a0, static_element_range_93282c, remove_range_3ca9d0)
CLEANUP(cleanup_element_range_7b3880, static_element_range_a120c0, remove_range_01d320)
CLEANUP(cleanup_element_range_7b38d0, static_element_range_a120cc, remove_range_013560)
