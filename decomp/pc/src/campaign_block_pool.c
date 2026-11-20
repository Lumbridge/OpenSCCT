struct Block { void *first, *limit; unsigned references, reserved; };
struct Blocks { unsigned reserved; struct Block *begin, *end, *capacity; };
struct Pointer { void *value; };
static __forceinline unsigned count(const struct Blocks *blocks)
{
    return blocks->begin ? (unsigned)(blocks->end - blocks->begin) : 0;
}

#define POP_BLOCK(Entry) \
void __fastcall Entry(struct Blocks *blocks) \
{ \
    if (count(blocks)) --blocks->end; \
}

#define CONTAINS_POINTER(Entry) \
unsigned char __fastcall Entry(struct Blocks *blocks, struct Pointer pointer) \
{ \
    struct Block *last; \
    if (!count(blocks)) return 0; \
    last = blocks->end - 1; \
    return (unsigned)pointer.value >= (unsigned)last->first && (unsigned)pointer.value < (unsigned)last->limit; \
}

#define RELEASE_ADDRESS(Entry, Pool, Contains, Drop, Free) \
    void __cdecl Entry(void *address) \
    { \
        struct Pointer pointer; \
        pointer.value = address; \
        if (Contains(&Pool, pointer)) Drop(&Pool, pointer); \
        else Free(address); \
    }

extern struct Blocks pool_90b6c4;
extern struct Blocks pool_923a04;
extern struct Blocks pool_a17b08;
extern struct Blocks pool_a17b44;
extern struct Blocks pool_a17b80;
extern struct Blocks pool_a17bbc;
extern struct Blocks pool_a17c24;
extern struct Blocks pool_a17c60;
extern struct Blocks pool_a17e64;
extern struct Blocks pool_a17ea0;
extern struct Blocks pool_a17eb4;
extern struct Blocks pool_a17ec8;
extern struct Blocks pool_a17edc;
extern struct Blocks pool_a18020;
extern struct Blocks pool_a1a0b0;
extern struct Blocks pool_a1a0ec;
extern struct Blocks pool_a1a128;
extern struct Blocks pool_a1a1e0;
extern struct Blocks pool_a1a21c;
extern struct Blocks pool_a1a258;
extern struct Blocks pool_a1a294;
extern struct Blocks pool_a1a2d0;
extern struct Blocks pool_a1a30c;
extern struct Blocks pool_a1a348;
extern struct Blocks pool_a1a384;
extern struct Blocks pool_a1a3fc;
extern struct Blocks pool_a1a488;
extern struct Blocks pool_a1a4c4;
extern struct Blocks pool_a1a500;
extern unsigned char __fastcall test_block_2133f0(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_213f90(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_427c40(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_427e20(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_428020(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_42fab0(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_430fd0(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_432b10(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_434200(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_435400(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_437dd0(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_43a4d0(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_43a620(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_43a770(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_44e210(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_44ed40(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_45b920(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_45d870(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_460710(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_461190(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_462140(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_462480(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_463b70(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_463e70(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_4641e0(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_465050(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_465e60(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_466140(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_4666d0(struct Blocks *, struct Pointer);
extern void __cdecl free_1dd0d0(void *);
extern void __fastcall drop_213430(struct Blocks *, struct Pointer);
extern void __fastcall drop_213fd0(struct Blocks *, struct Pointer);
extern void __fastcall drop_427c80(struct Blocks *, struct Pointer);
extern void __fastcall drop_427e60(struct Blocks *, struct Pointer);
extern void __fastcall drop_428060(struct Blocks *, struct Pointer);
extern void __fastcall drop_42faf0(struct Blocks *, struct Pointer);
extern void __fastcall drop_431010(struct Blocks *, struct Pointer);
extern void __fastcall drop_432b50(struct Blocks *, struct Pointer);
extern void __fastcall drop_434240(struct Blocks *, struct Pointer);
extern void __fastcall drop_435440(struct Blocks *, struct Pointer);
extern void __fastcall drop_437e10(struct Blocks *, struct Pointer);
extern void __fastcall drop_43a510(struct Blocks *, struct Pointer);
extern void __fastcall drop_43a660(struct Blocks *, struct Pointer);
extern void __fastcall drop_43a7b0(struct Blocks *, struct Pointer);
extern void __fastcall drop_44e250(struct Blocks *, struct Pointer);
extern void __fastcall drop_44ed80(struct Blocks *, struct Pointer);
extern void __fastcall drop_45b960(struct Blocks *, struct Pointer);
extern void __fastcall drop_45d8b0(struct Blocks *, struct Pointer);
extern void __fastcall drop_460750(struct Blocks *, struct Pointer);
extern void __fastcall drop_4611d0(struct Blocks *, struct Pointer);
extern void __fastcall drop_462180(struct Blocks *, struct Pointer);
extern void __fastcall drop_4624c0(struct Blocks *, struct Pointer);
extern void __fastcall drop_463bb0(struct Blocks *, struct Pointer);
extern void __fastcall drop_463eb0(struct Blocks *, struct Pointer);
extern void __fastcall drop_464220(struct Blocks *, struct Pointer);
extern void __fastcall drop_465090(struct Blocks *, struct Pointer);
extern void __fastcall drop_465ea0(struct Blocks *, struct Pointer);
extern void __fastcall drop_466180(struct Blocks *, struct Pointer);
extern void __fastcall drop_466710(struct Blocks *, struct Pointer);

CONTAINS_POINTER(contains_1dd8e0)
POP_BLOCK(pop_1dd960)
RELEASE_ADDRESS(release_2133c0, pool_90b6c4, test_block_2133f0, drop_213430, free_1dd0d0)
CONTAINS_POINTER(contains_2133f0)
POP_BLOCK(pop_213470)
CONTAINS_POINTER(contains_213c10)
POP_BLOCK(pop_213c90)
RELEASE_ADDRESS(release_213f60, pool_923a04, test_block_213f90, drop_213fd0, free_1dd0d0)
CONTAINS_POINTER(contains_213f90)
POP_BLOCK(pop_214010)
CONTAINS_POINTER(contains_427b50)
POP_BLOCK(pop_427bd0)
RELEASE_ADDRESS(release_427c10, pool_a1a294, test_block_427c40, drop_427c80, free_1dd0d0)
CONTAINS_POINTER(contains_427c40)
POP_BLOCK(pop_427cc0)
CONTAINS_POINTER(contains_427d30)
POP_BLOCK(pop_427db0)
RELEASE_ADDRESS(release_427df0, pool_a1a2d0, test_block_427e20, drop_427e60, free_1dd0d0)
CONTAINS_POINTER(contains_427e20)
POP_BLOCK(pop_427ea0)
RELEASE_ADDRESS(release_427ff0, pool_a1a1e0, test_block_428020, drop_428060, free_1dd0d0)
CONTAINS_POINTER(contains_428020)
POP_BLOCK(pop_4280a0)
RELEASE_ADDRESS(release_42fa80, pool_a17b08, test_block_42fab0, drop_42faf0, free_1dd0d0)
CONTAINS_POINTER(contains_42fab0)
POP_BLOCK(pop_42fb30)
RELEASE_ADDRESS(release_430fa0, pool_a17b44, test_block_430fd0, drop_431010, free_1dd0d0)
CONTAINS_POINTER(contains_430fd0)
POP_BLOCK(pop_431050)
RELEASE_ADDRESS(release_432ae0, pool_a17b80, test_block_432b10, drop_432b50, free_1dd0d0)
CONTAINS_POINTER(contains_432b10)
POP_BLOCK(pop_432b90)
RELEASE_ADDRESS(release_4341d0, pool_a17bbc, test_block_434200, drop_434240, free_1dd0d0)
CONTAINS_POINTER(contains_434200)
POP_BLOCK(pop_434280)
RELEASE_ADDRESS(release_4353d0, pool_a17c24, test_block_435400, drop_435440, free_1dd0d0)
CONTAINS_POINTER(contains_435400)
POP_BLOCK(pop_435480)
RELEASE_ADDRESS(release_437da0, pool_a17c60, test_block_437dd0, drop_437e10, free_1dd0d0)
CONTAINS_POINTER(contains_437dd0)
POP_BLOCK(pop_437e50)
CONTAINS_POINTER(contains_4388f0)
POP_BLOCK(pop_438970)
CONTAINS_POINTER(contains_438d50)
POP_BLOCK(pop_438dd0)
RELEASE_ADDRESS(release_43a4a0, pool_a17ea0, test_block_43a4d0, drop_43a510, free_1dd0d0)
CONTAINS_POINTER(contains_43a4d0)
POP_BLOCK(pop_43a550)
RELEASE_ADDRESS(release_43a5f0, pool_a17ec8, test_block_43a620, drop_43a660, free_1dd0d0)
CONTAINS_POINTER(contains_43a620)
POP_BLOCK(pop_43a6a0)
RELEASE_ADDRESS(release_43a740, pool_a17eb4, test_block_43a770, drop_43a7b0, free_1dd0d0)
CONTAINS_POINTER(contains_43a770)
POP_BLOCK(pop_43a7f0)
RELEASE_ADDRESS(release_44e1e0, pool_a17e64, test_block_44e210, drop_44e250, free_1dd0d0)
CONTAINS_POINTER(contains_44e210)
POP_BLOCK(pop_44e290)
CONTAINS_POINTER(contains_44ea90)
POP_BLOCK(pop_44eb10)
RELEASE_ADDRESS(release_44ed10, pool_a17edc, test_block_44ed40, drop_44ed80, free_1dd0d0)
CONTAINS_POINTER(contains_44ed40)
POP_BLOCK(pop_44edc0)
CONTAINS_POINTER(contains_459a30)
POP_BLOCK(pop_459ab0)
RELEASE_ADDRESS(release_45b8f0, pool_a18020, test_block_45b920, drop_45b960, free_1dd0d0)
CONTAINS_POINTER(contains_45b920)
POP_BLOCK(pop_45b9a0)
RELEASE_ADDRESS(release_45d840, pool_a1a0b0, test_block_45d870, drop_45d8b0, free_1dd0d0)
CONTAINS_POINTER(contains_45d870)
POP_BLOCK(pop_45d8f0)
RELEASE_ADDRESS(release_4606e0, pool_a1a0ec, test_block_460710, drop_460750, free_1dd0d0)
CONTAINS_POINTER(contains_460710)
POP_BLOCK(pop_460790)
RELEASE_ADDRESS(release_461160, pool_a1a128, test_block_461190, drop_4611d0, free_1dd0d0)
CONTAINS_POINTER(contains_461190)
POP_BLOCK(pop_461210)
RELEASE_ADDRESS(release_462110, pool_a1a21c, test_block_462140, drop_462180, free_1dd0d0)
CONTAINS_POINTER(contains_462140)
POP_BLOCK(pop_4621c0)
RELEASE_ADDRESS(release_462450, pool_a1a258, test_block_462480, drop_4624c0, free_1dd0d0)
CONTAINS_POINTER(contains_462480)
POP_BLOCK(pop_462500)
RELEASE_ADDRESS(release_463b40, pool_a1a30c, test_block_463b70, drop_463bb0, free_1dd0d0)
CONTAINS_POINTER(contains_463b70)
POP_BLOCK(pop_463bf0)
RELEASE_ADDRESS(release_463e40, pool_a1a348, test_block_463e70, drop_463eb0, free_1dd0d0)
CONTAINS_POINTER(contains_463e70)
POP_BLOCK(pop_463ef0)
RELEASE_ADDRESS(release_4641b0, pool_a1a384, test_block_4641e0, drop_464220, free_1dd0d0)
CONTAINS_POINTER(contains_4641e0)
POP_BLOCK(pop_464260)
CONTAINS_POINTER(contains_4645e0)
POP_BLOCK(pop_464660)
RELEASE_ADDRESS(release_465020, pool_a1a3fc, test_block_465050, drop_465090, free_1dd0d0)
CONTAINS_POINTER(contains_465050)
POP_BLOCK(pop_4650d0)
RELEASE_ADDRESS(release_465e30, pool_a1a488, test_block_465e60, drop_465ea0, free_1dd0d0)
CONTAINS_POINTER(contains_465e60)
POP_BLOCK(pop_465ee0)
RELEASE_ADDRESS(release_466110, pool_a1a4c4, test_block_466140, drop_466180, free_1dd0d0)
CONTAINS_POINTER(contains_466140)
POP_BLOCK(pop_4661c0)
RELEASE_ADDRESS(release_4666a0, pool_a1a500, test_block_4666d0, drop_466710, free_1dd0d0)
CONTAINS_POINTER(contains_4666d0)
POP_BLOCK(pop_466750)
