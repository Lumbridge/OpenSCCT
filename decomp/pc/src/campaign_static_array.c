#include "campaign_array_storage.h"
/* The recovered member ABI puts these integer bit patterns on the stack. */
union StackWord { int integer; float argument; };

#define CLEANUP(Entry, Object, Remove, Width) \
void Entry(void) \
{ \
    static const union StackWord start = { 0 }, stride = { Width }; \
    struct ArraySize count; \
    count.value = Object.count; \
    Remove(&Object, start.argument, count, stride.argument); \
    release_storage(&Object); \
}

extern struct Array static_array_8f5608;
extern struct Array static_array_8f5614;
extern struct Array static_array_8f5efc;
extern struct Array static_array_8f5f08;
extern struct Array static_array_8f5ff0;
extern struct Array static_array_8fa4cc;
extern struct Array static_array_8fa7f8;
extern struct Array static_array_92ed0c;
extern struct Array static_array_a0dfc0;
extern struct Array static_array_a0dfcc;
extern struct Array static_array_a12078;
extern struct Array static_array_a12084;
extern struct Array static_array_a12090;
extern struct Array static_array_a1209c;
extern struct Array static_array_a120a8;
extern struct Array static_array_a120b4;
extern struct Array static_array_a17078;
extern struct Array static_array_a17084;
extern struct Array static_array_a17090;
extern void __fastcall remove_range_00b820(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_015cf0(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_01b2c0(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_029170(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_05f5c0(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_05f6a0(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_05f7b0(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_05f890(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_066210(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_077690(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_1b6ad0(struct Array *, float start, struct ArraySize count, float stride);
extern void __fastcall remove_range_3d4490(struct Array *, float start, struct ArraySize count, float stride);

CLEANUP(cleanup_array_7b05c0, static_array_8f5608, remove_range_05f7b0, 4)
CLEANUP(cleanup_array_7b0610, static_array_8f5614, remove_range_05f890, 4)
CLEANUP(cleanup_array_7b0660, static_array_8f5efc, remove_range_05f5c0, 4)
CLEANUP(cleanup_array_7b06b0, static_array_8f5f08, remove_range_05f6a0, 4)
CLEANUP(cleanup_array_7b0790, static_array_8f5ff0, remove_range_077690, 4)
CLEANUP(cleanup_array_7b0d40, static_array_8fa4cc, remove_range_029170, 4)
CLEANUP(cleanup_array_7b0d90, static_array_8fa7f8, remove_range_1b6ad0, 4)
CLEANUP(cleanup_array_7b33a0, static_array_92ed0c, remove_range_066210, 1)
CLEANUP(cleanup_array_7b3650, static_array_a0dfc0, remove_range_3d4490, 4)
CLEANUP(cleanup_array_7b3600, static_array_a0dfcc, remove_range_029170, 4)
CLEANUP(cleanup_array_7b36a0, static_array_a12078, remove_range_01b2c0, 4)
CLEANUP(cleanup_array_7b36f0, static_array_a12084, remove_range_01b2c0, 4)
CLEANUP(cleanup_array_7b3830, static_array_a12090, remove_range_029170, 4)
CLEANUP(cleanup_array_7b3740, static_array_a1209c, remove_range_01b2c0, 4)
CLEANUP(cleanup_array_7b3790, static_array_a120a8, remove_range_01b2c0, 4)
CLEANUP(cleanup_array_7b37e0, static_array_a120b4, remove_range_01b2c0, 4)
CLEANUP(cleanup_array_7b3930, static_array_a17090, remove_range_00b820, 4)
CLEANUP(cleanup_array_7b3980, static_array_a17078, remove_range_00b820, 4)
CLEANUP(cleanup_array_7b39d0, static_array_a17084, remove_range_015cf0, 4)
