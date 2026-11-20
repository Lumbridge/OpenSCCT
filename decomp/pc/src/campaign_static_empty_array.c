#include "campaign_array_resize_abi.h"
union StackWord { int integer; float argument; };

#define CLEANUP(Entry, Object, Remove, Width, Tag) \
void Entry(void) \
{ \
    static const union StackWord stride = { Width }; \
    struct ArraySize count; \
    struct ArrayTag tag; \
    tag.text = Tag; \
    Object.count = 0; \
    Object.capacity = 0; \
    Object.buffer.bytes = resize_heap->methods->resize(resize_heap, Object.buffer.bytes, Object.buffer, 0, tag); \
    count.value = Object.count; \
    Remove(&Object, 0.0f, count, stride.argument); \
    release_storage(&Object); \
}

extern const unsigned short array_tag_7b78d8[];
extern struct Array static_empty_array_90a9e4;
extern struct Array static_empty_array_90a9f0;
extern struct Array static_empty_array_90a9fc;
extern struct Array static_empty_array_90aa08;
extern struct Array static_empty_array_90aa14;
extern struct Array static_empty_array_90aa20;
extern struct Array static_empty_array_90aa2c;
extern struct Array static_empty_array_90aa38;
extern struct Array static_empty_array_90aa44;
extern struct Array static_empty_array_90aa50;
extern struct Array static_empty_array_92d614;
extern struct Array static_empty_array_92d620;
extern struct Array static_empty_array_92d62c;
extern struct Array static_empty_array_92d638;
extern struct Array static_empty_array_92da60;
extern struct Array static_empty_array_92dba8;
extern struct Array static_empty_array_92dbb4;
extern struct Array static_empty_array_92dbc0;
extern struct Array static_empty_array_92dd64;
extern struct Array static_empty_array_92e5b8;
extern struct Array static_empty_array_92e5c4;
extern struct Array static_empty_array_92e5d0;
extern struct Array static_empty_array_92e5dc;
extern struct Array static_empty_array_92f954;
extern void __fastcall remove_range_007710(struct Array *, float start, struct ArraySize count, float stride);

CLEANUP(cleanup_empty_array_7b18f0, static_empty_array_92d638, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b1880, static_empty_array_92d62c, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b1810, static_empty_array_92d620, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b17a0, static_empty_array_92d614, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b3330, static_empty_array_92e5dc, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b32c0, static_empty_array_92e5d0, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b3250, static_empty_array_92e5c4, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b31e0, static_empty_array_92e5b8, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b3490, static_empty_array_92f954, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b0e50, static_empty_array_90a9e4, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b0ec0, static_empty_array_90a9f0, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b0f30, static_empty_array_90a9fc, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b0fa0, static_empty_array_90aa08, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b1010, static_empty_array_90aa14, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b1080, static_empty_array_90aa20, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b10f0, static_empty_array_90aa2c, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b1160, static_empty_array_90aa38, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b11d0, static_empty_array_90aa44, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b1240, static_empty_array_90aa50, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b1f10, static_empty_array_92da60, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b21f0, static_empty_array_92dba8, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b2260, static_empty_array_92dbb4, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b22d0, static_empty_array_92dbc0, remove_range_007710, 2, array_tag_7b78d8)
CLEANUP(cleanup_empty_array_7b2680, static_empty_array_92dd64, remove_range_007710, 2, array_tag_7b78d8)
