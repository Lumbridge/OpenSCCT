#include "campaign_array_storage.h"

union StackWord { int integer; float argument; };
struct Owner4 { unsigned char prefix[4]; struct Array records; };
struct Owner8 { unsigned char prefix[8]; struct Array records; };
struct Owner12 { unsigned char prefix[12]; struct Array records; };
struct Owner16 { unsigned char prefix[16]; struct Array records; };
struct Owner20 { unsigned char prefix[20]; struct Array records; };
struct Owner24 { unsigned char prefix[24]; struct Array records; };
struct Owner32 { unsigned char prefix[32]; struct Array records; };
extern void __fastcall remove_007710(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_029170(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_029260(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_029a90(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_066210(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_077960(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_07d430(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0802d0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_091ae0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_09df90(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0bf190(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0da200(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_0da320(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_121790(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1256a0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_154a40(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_16ead0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1712c0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1728b0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1a9440(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1a94d0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_1ce9d0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_2db4e0(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_2db690(struct Array *, float, struct ArraySize, float);
extern void __fastcall remove_2e5ed0(struct Array *, float, struct ArraySize, float);

#define CLEANUP(Entry, Offset, Remove, Width) \
    void __fastcall Entry(struct Owner##Offset *owner) \
    { \
        static const union StackWord first = { 0 }; \
        static const union StackWord stride = { Width }; \
        struct Array *array = &owner->records; \
        struct ArraySize count; \
        count.value = array->count; \
        Remove(array, first.argument, count, stride.argument); \
        release_storage(array); \
    }

CLEANUP(cleanup_055330, 20, remove_029a90, 4)
CLEANUP(cleanup_073a00, 16, remove_029260, 12)
CLEANUP(cleanup_07dbf0, 4, remove_07d430, 16)
CLEANUP(cleanup_0813d0, 4, remove_029260, 12)
CLEANUP(cleanup_082650, 20, remove_007710, 2)
CLEANUP(cleanup_082a60, 20, remove_0802d0, 28)
CLEANUP(cleanup_091aa0, 8, remove_029170, 4)
CLEANUP(cleanup_09e970, 12, remove_09df90, 4)
CLEANUP(cleanup_0ad390, 32, remove_007710, 2)
CLEANUP(cleanup_0bf570, 4, remove_066210, 1)
CLEANUP(cleanup_0c19e0, 20, remove_077960, 4)
CLEANUP(cleanup_0c1bd0, 20, remove_0bf190, 28)
CLEANUP(cleanup_0c1c10, 20, remove_077960, 4)
CLEANUP(cleanup_0e4da0, 24, remove_007710, 2)
CLEANUP(cleanup_0e4ee0, 12, remove_0da200, 16)
CLEANUP(cleanup_0ebae0, 12, remove_0da320, 8)
CLEANUP(cleanup_121fc0, 20, remove_121790, 20)
CLEANUP(cleanup_122810, 20, remove_121790, 20)
CLEANUP(cleanup_124600, 20, remove_121790, 20)
CLEANUP(cleanup_126af0, 20, remove_1256a0, 32)
CLEANUP(cleanup_127f60, 20, remove_121790, 20)
CLEANUP(cleanup_128080, 20, remove_1256a0, 32)
CLEANUP(cleanup_156670, 20, remove_154a40, 28)
CLEANUP(cleanup_15bd70, 4, remove_029170, 4)
CLEANUP(cleanup_16f080, 4, remove_066210, 1)
CLEANUP(cleanup_16f7c0, 24, remove_007710, 2)
CLEANUP(cleanup_16f800, 24, remove_007710, 2)
CLEANUP(cleanup_170020, 20, remove_16ead0, 40)
CLEANUP(cleanup_171560, 20, remove_1712c0, 28)
CLEANUP(cleanup_174c50, 20, remove_1728b0, 24)
CLEANUP(cleanup_1aa590, 4, remove_1a9440, 4)
CLEANUP(cleanup_1aa5d0, 8, remove_1a94d0, 4)
CLEANUP(cleanup_1cf260, 4, remove_1ce9d0, 4)
CLEANUP(cleanup_25e700, 8, remove_029170, 4)
CLEANUP(cleanup_2e2870, 4, remove_2db690, 20)
CLEANUP(cleanup_2e88c0, 12, remove_029a90, 4)
CLEANUP(cleanup_314d20, 12, remove_2db4e0, 4)
CLEANUP(cleanup_31f380, 4, remove_091ae0, 4)
CLEANUP(cleanup_325c90, 4, remove_029170, 4)
CLEANUP(cleanup_34d8b0, 20, remove_2e5ed0, 4)
