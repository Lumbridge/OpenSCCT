struct Blocks;
struct Pointer { void *value; };

#define RELEASE_ADDRESS(Entry, Pool, Contains, Drop, Free) \
    void __cdecl Entry(void *address) \
    { \
        struct Pointer pointer; \
        pointer.value = address; \
        if (Contains(&Pool, pointer)) Drop(&Pool, pointer); \
        else Free(address); \
    }

extern struct Blocks pool_90b730;
extern struct Blocks pool_923a18;
extern struct Blocks pool_a17cb4;
extern struct Blocks pool_a17cf0;
extern struct Blocks pool_a17ef0;
extern struct Blocks pool_a17fe4;
extern struct Blocks pool_a1a168;
extern struct Blocks pool_a1a1a4;
extern struct Blocks pool_a1a3c0;
extern unsigned char __fastcall test_block_1dd8e0(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_213c10(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_427b50(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_427d30(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_4388f0(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_438d50(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_44ea90(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_459a30(struct Blocks *, struct Pointer);
extern unsigned char __fastcall test_block_4645e0(struct Blocks *, struct Pointer);
extern void __cdecl free_1dd0d0(void *);
extern void __fastcall drop_1dd920(struct Blocks *, struct Pointer);
extern void __fastcall drop_213c50(struct Blocks *, struct Pointer);
extern void __fastcall drop_427b90(struct Blocks *, struct Pointer);
extern void __fastcall drop_427d70(struct Blocks *, struct Pointer);
extern void __fastcall drop_438930(struct Blocks *, struct Pointer);
extern void __fastcall drop_438d90(struct Blocks *, struct Pointer);
extern void __fastcall drop_44ead0(struct Blocks *, struct Pointer);
extern void __fastcall drop_459a70(struct Blocks *, struct Pointer);
extern void __fastcall drop_464620(struct Blocks *, struct Pointer);

RELEASE_ADDRESS(release_1dd8b0, pool_90b730, test_block_1dd8e0, drop_1dd920, free_1dd0d0)
RELEASE_ADDRESS(release_213be0, pool_923a18, test_block_213c10, drop_213c50, free_1dd0d0)
RELEASE_ADDRESS(release_427b20, pool_a1a168, test_block_427b50, drop_427b90, free_1dd0d0)
RELEASE_ADDRESS(release_427d00, pool_a1a1a4, test_block_427d30, drop_427d70, free_1dd0d0)
RELEASE_ADDRESS(release_4388c0, pool_a17cb4, test_block_4388f0, drop_438930, free_1dd0d0)
RELEASE_ADDRESS(release_438d20, pool_a17cf0, test_block_438d50, drop_438d90, free_1dd0d0)
RELEASE_ADDRESS(release_44ea60, pool_a17ef0, test_block_44ea90, drop_44ead0, free_1dd0d0)
RELEASE_ADDRESS(release_459a00, pool_a17fe4, test_block_459a30, drop_459a70, free_1dd0d0)
RELEASE_ADDRESS(release_4645b0, pool_a1a3c0, test_block_4645e0, drop_464620, free_1dd0d0)
