#include "campaign_array_storage.h"

struct Owner { struct Array records; struct ArrayBuffer auxiliary; int auxiliary_count; };
struct DeleteFlags { unsigned value; };
union StackWord { int integer; float argument; };

extern void __fastcall remove_007710(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_019a00(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_029170(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_029a90(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_077690(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_077840(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0778d0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_07d830(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0beb60(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0bedf0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0d9760(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0d9db0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_16eb60(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1ade50(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1ae2a0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1e0a90(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_236190(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_306020(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_350920(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_3da0d0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_3e53a0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_3e7ff0(struct Array *, float, struct ArraySize, float);

#define DEFINE_DELETING_ARRAY(Entry, Remove, Width) \
struct Array *__fastcall Entry(struct Array *array, struct DeleteFlags flags) \
{ \
    static const union StackWord first = { 0 }; \
    static const union StackWord stride = { Width }; \
    struct ArraySize count; \
    count.value = array->count; \
    Remove(array, first.argument, count, stride.argument); \
    release_storage(array); \
    if (flags.value & 1) { \
        struct ArrayBuffer storage; \
        storage.bytes = (unsigned char *)array; \
        array_allocator->methods->release(array_allocator, storage); \
    } \
    return array; \
}

#define DEFINE_OWNER_CLEANUP(Entry, Remove, Width) \
void __fastcall Entry(struct Owner *owner) \
{ \
    static const union StackWord first = { 0 }; \
    static const union StackWord stride = { Width }; \
    struct ArraySize count; \
    if (owner->auxiliary.bytes) { \
        struct ArrayBuffer storage; \
        storage.bytes = owner->auxiliary.bytes; \
        array_allocator->methods->release(array_allocator, storage); \
    } \
    owner->auxiliary.bytes = 0; \
    owner->auxiliary_count = 0; \
    count.value = owner->records.count; \
    Remove(&owner->records, first.argument, count, stride.argument); \
    release_storage(&owner->records); \
}

DEFINE_DELETING_ARRAY(cleanup_078080, remove_019a00, 4)
DEFINE_DELETING_ARRAY(cleanup_078840, remove_077690, 4)
DEFINE_OWNER_CLEANUP(cleanup_07adf0, remove_077840, 12)
DEFINE_OWNER_CLEANUP(cleanup_07aea0, remove_0778d0, 12)
DEFINE_OWNER_CLEANUP(cleanup_07b150, remove_077840, 12)
DEFINE_OWNER_CLEANUP(cleanup_07b1c0, remove_0778d0, 12)
DEFINE_OWNER_CLEANUP(cleanup_07dc80, remove_07d830, 8)
DEFINE_OWNER_CLEANUP(cleanup_07ddd0, remove_07d830, 8)
DEFINE_DELETING_ARRAY(cleanup_0c0d40, remove_0beb60, 4)
DEFINE_OWNER_CLEANUP(cleanup_0c1910, remove_0bedf0, 12)
DEFINE_OWNER_CLEANUP(cleanup_0c1d00, remove_0bedf0, 12)
DEFINE_DELETING_ARRAY(cleanup_0e4d50, remove_007710, 2)
DEFINE_OWNER_CLEANUP(cleanup_0eb3d0, remove_0d9db0, 8)
DEFINE_DELETING_ARRAY(cleanup_0eba00, remove_0d9760, 16)
DEFINE_OWNER_CLEANUP(cleanup_0f3250, remove_0d9db0, 8)
DEFINE_DELETING_ARRAY(cleanup_1701e0, remove_16eb60, 56)
DEFINE_OWNER_CLEANUP(cleanup_1afe80, remove_1ae2a0, 12)
DEFINE_OWNER_CLEANUP(cleanup_1b02d0, remove_1ae2a0, 12)
DEFINE_DELETING_ARRAY(cleanup_1b6010, remove_029a90, 4)
DEFINE_OWNER_CLEANUP(cleanup_1de790, remove_1ade50, 12)
DEFINE_OWNER_CLEANUP(cleanup_1de870, remove_1ade50, 12)
DEFINE_DELETING_ARRAY(cleanup_1e1860, remove_1e0a90, 4)
DEFINE_DELETING_ARRAY(cleanup_2dcf50, remove_029170, 4)
DEFINE_DELETING_ARRAY(cleanup_306990, remove_306020, 12)
DEFINE_DELETING_ARRAY(cleanup_327400, remove_236190, 4)
DEFINE_DELETING_ARRAY(cleanup_350c10, remove_350920, 4)
DEFINE_DELETING_ARRAY(cleanup_372330, remove_029170, 4)
DEFINE_OWNER_CLEANUP(cleanup_3dd0f0, remove_3da0d0, 12)
DEFINE_OWNER_CLEANUP(cleanup_3ddf10, remove_3da0d0, 12)
DEFINE_OWNER_CLEANUP(cleanup_3e6730, remove_3e53a0, 12)
DEFINE_OWNER_CLEANUP(cleanup_3e67a0, remove_3e53a0, 12)
DEFINE_OWNER_CLEANUP(cleanup_3e8430, remove_3e7ff0, 12)
DEFINE_OWNER_CLEANUP(cleanup_3e8510, remove_3e7ff0, 12)
