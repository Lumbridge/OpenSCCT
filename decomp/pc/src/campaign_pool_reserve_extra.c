struct Block { void *base, *cursor; unsigned used, capacity; };
struct Pool { unsigned reserved; struct Block *first, *end, *limit; unsigned char active; };
struct Word { unsigned value; };
struct BlockArg { struct Block *value; };
extern void *__cdecl allocate_block(unsigned bytes, unsigned mode);

extern void __fastcall append_21e9b0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a0e0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a1c0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a3f0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a700(struct Pool *, struct BlockArg);
extern void __fastcall append_44a8c0(struct Pool *, struct BlockArg);
extern void __fastcall append_44ab60(struct Pool *, struct BlockArg);
extern void __fastcall append_44ac40(struct Pool *, struct BlockArg);
extern void __fastcall append_44aee0(struct Pool *, struct BlockArg);

#define RESERVE_GLOBAL(Entry, Append, Stride, GlobalPool) \
    void __stdcall Entry(unsigned count) \
    { \
        if (count > 0) { \
            struct Block block; \
            struct BlockArg arg; \
            block.base = allocate_block(count * Stride, 0); \
            block.cursor = block.base; \
            block.used = 0; \
            block.capacity = count; \
            arg.value = &block; \
            Append(&GlobalPool, arg); \
            GlobalPool.active = 1; \
        } \
    }

#define RESERVE_MEMBER(Entry, Append, Stride) \
    void __fastcall Entry(struct Pool *self, struct Word count, struct Word mode) \
    { \
        if (count.value > 0) { \
            struct Block block; \
            struct BlockArg arg; \
            block.base = allocate_block(count.value * Stride, mode.value); \
            block.cursor = block.base; \
            block.used = 0; \
            block.capacity = count.value; \
            arg.value = &block; \
            Append(self, arg); \
            self->active = 1; \
        } \
    }

RESERVE_MEMBER(reserve_21eb60, append_21e9b0, 40)
RESERVE_MEMBER(reserve_44b0f0, append_44a0e0, 36)
RESERVE_MEMBER(reserve_44b190, append_44a1c0, 24)
RESERVE_MEMBER(reserve_44b320, append_44a3f0, 12)
RESERVE_MEMBER(reserve_44b550, append_44a700, 80)
RESERVE_MEMBER(reserve_44b690, append_44a8c0, 40)
RESERVE_MEMBER(reserve_44b890, append_44ab60, 36)
RESERVE_MEMBER(reserve_44b930, append_44ac40, 192)
RESERVE_MEMBER(reserve_44bb10, append_44aee0, 40)
