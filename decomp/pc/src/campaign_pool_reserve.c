struct Block { void *base, *cursor; unsigned used, capacity; };
struct Pool { unsigned reserved; struct Block *first, *end, *limit; unsigned char active; };
struct Word { unsigned value; };
struct BlockArg { struct Block *value; };
extern void *__cdecl allocate_block(unsigned bytes, unsigned mode);

extern void __fastcall append_21e7f0(struct Pool *, struct BlockArg);
extern void __fastcall append_21e860(struct Pool *, struct BlockArg);
extern void __fastcall append_21e8d0(struct Pool *, struct BlockArg);
extern void __fastcall append_21e940(struct Pool *, struct BlockArg);
extern void __fastcall append_21e9b0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a0e0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a150(struct Pool *, struct BlockArg);
extern void __fastcall append_44a1c0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a230(struct Pool *, struct BlockArg);
extern void __fastcall append_44a2a0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a310(struct Pool *, struct BlockArg);
extern void __fastcall append_44a380(struct Pool *, struct BlockArg);
extern void __fastcall append_44a3f0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a460(struct Pool *, struct BlockArg);
extern void __fastcall append_44a4d0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a540(struct Pool *, struct BlockArg);
extern void __fastcall append_44a5b0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a620(struct Pool *, struct BlockArg);
extern void __fastcall append_44a690(struct Pool *, struct BlockArg);
extern void __fastcall append_44a700(struct Pool *, struct BlockArg);
extern void __fastcall append_44a770(struct Pool *, struct BlockArg);
extern void __fastcall append_44a7e0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a850(struct Pool *, struct BlockArg);
extern void __fastcall append_44a8c0(struct Pool *, struct BlockArg);
extern void __fastcall append_44a930(struct Pool *, struct BlockArg);
extern void __fastcall append_44a9a0(struct Pool *, struct BlockArg);
extern void __fastcall append_44aa10(struct Pool *, struct BlockArg);
extern void __fastcall append_44aa80(struct Pool *, struct BlockArg);
extern void __fastcall append_44aaf0(struct Pool *, struct BlockArg);
extern void __fastcall append_44ab60(struct Pool *, struct BlockArg);
extern void __fastcall append_44abd0(struct Pool *, struct BlockArg);
extern void __fastcall append_44ac40(struct Pool *, struct BlockArg);
extern void __fastcall append_44acb0(struct Pool *, struct BlockArg);
extern void __fastcall append_44ad20(struct Pool *, struct BlockArg);
extern void __fastcall append_44ad90(struct Pool *, struct BlockArg);
extern void __fastcall append_44ae00(struct Pool *, struct BlockArg);
extern void __fastcall append_44ae70(struct Pool *, struct BlockArg);
extern void __fastcall append_44aee0(struct Pool *, struct BlockArg);
extern void __fastcall append_44af50(struct Pool *, struct BlockArg);
extern void __fastcall append_44bbb0(struct Pool *, struct BlockArg);
extern void __fastcall append_44bc20(struct Pool *, struct BlockArg);
extern struct Pool pool_90b6c4;
extern struct Pool pool_923a04;
extern struct Pool pool_923a18;
extern struct Pool pool_92d3e4;
extern struct Pool pool_a17b08;
extern struct Pool pool_a17b44;
extern struct Pool pool_a17b80;
extern struct Pool pool_a17bbc;
extern struct Pool pool_a17c24;
extern struct Pool pool_a17c60;
extern struct Pool pool_a17c74;
extern struct Pool pool_a17cb4;
extern struct Pool pool_a17cf0;
extern struct Pool pool_a17e00;
extern struct Pool pool_a17e64;
extern struct Pool pool_a17ea0;
extern struct Pool pool_a17eb4;
extern struct Pool pool_a17ec8;
extern struct Pool pool_a17edc;
extern struct Pool pool_a17ef0;
extern struct Pool pool_a17fe4;
extern struct Pool pool_a18020;
extern struct Pool pool_a1a0b0;
extern struct Pool pool_a1a0ec;
extern struct Pool pool_a1a128;
extern struct Pool pool_a1a168;
extern struct Pool pool_a1a1a4;
extern struct Pool pool_a1a1e0;
extern struct Pool pool_a1a21c;
extern struct Pool pool_a1a258;
extern struct Pool pool_a1a294;
extern struct Pool pool_a1a2d0;
extern struct Pool pool_a1a348;
extern struct Pool pool_a1a384;
extern struct Pool pool_a1a3c0;
extern struct Pool pool_a1a3fc;
extern struct Pool pool_a1a488;
extern struct Pool pool_a1a4c4;
extern struct Pool pool_a1a500;

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

RESERVE_MEMBER(reserve_21ea20, append_21e7f0, 28)
RESERVE_MEMBER(reserve_21ea70, append_21e860, 28)
RESERVE_MEMBER(reserve_21eac0, append_21e8d0, 8)
RESERVE_MEMBER(reserve_21eb10, append_21e940, 52)
RESERVE_GLOBAL(reserve_21ec00, append_21e940, 52, pool_90b6c4)
RESERVE_GLOBAL(reserve_21ec50, append_21e9b0, 40, pool_92d3e4)
RESERVE_GLOBAL(reserve_21eca0, append_21e860, 28, pool_923a04)
RESERVE_GLOBAL(reserve_21ecf0, append_21e8d0, 8, pool_923a18)
RESERVE_MEMBER(reserve_44b280, append_44a310, 8)
RESERVE_MEMBER(reserve_44b2d0, append_44a380, 64)
RESERVE_MEMBER(reserve_44b410, append_44a540, 88)
RESERVE_MEMBER(reserve_44b460, append_44a5b0, 92)
RESERVE_MEMBER(reserve_44b4b0, append_44a620, 16)
RESERVE_MEMBER(reserve_44b500, append_44a690, 28)
RESERVE_MEMBER(reserve_44b5f0, append_44a7e0, 52)
RESERVE_MEMBER(reserve_44b640, append_44a850, 92)
RESERVE_MEMBER(reserve_44b740, append_44a9a0, 16)
RESERVE_MEMBER(reserve_44b790, append_44aa10, 28)
RESERVE_MEMBER(reserve_44b7e0, append_44aa80, 92)
RESERVE_MEMBER(reserve_44b830, append_44aaf0, 208)
RESERVE_MEMBER(reserve_44ba70, append_44ae00, 92)
RESERVE_MEMBER(reserve_44bac0, append_44ae70, 28)
RESERVE_GLOBAL(reserve_44c700, append_44acb0, 20, pool_a17cb4)
RESERVE_GLOBAL(reserve_44c750, append_44a0e0, 36, pool_a17b44)
RESERVE_GLOBAL(reserve_44c7a0, append_44af50, 84, pool_a17b80)
RESERVE_GLOBAL(reserve_44c7f0, append_44a4d0, 52, pool_a1a128)
RESERVE_GLOBAL(reserve_44c840, append_44a7e0, 52, pool_a1a384)
RESERVE_GLOBAL(reserve_44c890, append_44a8c0, 40, pool_a1a1e0)
RESERVE_GLOBAL(reserve_44c8e0, append_44a690, 28, pool_a17c60)
RESERVE_GLOBAL(reserve_44c930, append_44a700, 80, pool_a17bbc)
RESERVE_GLOBAL(reserve_44c980, append_44a770, 84, pool_a1a348)
RESERVE_GLOBAL(reserve_44c9d0, append_44ad90, 84, pool_a1a3fc)
RESERVE_GLOBAL(reserve_44ca20, append_44a3f0, 12, pool_a1a168)
RESERVE_GLOBAL(reserve_44ca70, append_44a9a0, 16, pool_a1a1a4)
RESERVE_GLOBAL(reserve_44cac0, append_44aa10, 28, pool_a1a3c0)
RESERVE_GLOBAL(reserve_44cb10, append_44ab60, 36, pool_a17cf0)
RESERVE_GLOBAL(reserve_44cb60, append_44ae70, 28, pool_a17fe4)
RESERVE_GLOBAL(reserve_44cbb0, append_44abd0, 52, pool_a17e64)
RESERVE_GLOBAL(reserve_44cc00, append_44aa80, 92, pool_a1a0b0)
RESERVE_GLOBAL(reserve_44cc50, append_44aee0, 40, pool_a1a4c4)
RESERVE_GLOBAL(reserve_44cca0, append_44aaf0, 208, pool_a17c24)
RESERVE_GLOBAL(reserve_44ccf0, append_44ac40, 192, pool_a17b08)
RESERVE_GLOBAL(reserve_44cd40, append_44a930, 204, pool_a1a0ec)
RESERVE_GLOBAL(reserve_44cd90, append_44ad20, 24, pool_a1a488)
RESERVE_GLOBAL(reserve_44cde0, append_44a460, 80, pool_a18020)
RESERVE_GLOBAL(reserve_44ce30, append_44a540, 88, pool_a1a258)
RESERVE_GLOBAL(reserve_44ce80, append_44a5b0, 92, pool_a1a21c)
RESERVE_GLOBAL(reserve_44ced0, append_44a850, 92, pool_a1a294)
RESERVE_GLOBAL(reserve_44cf20, append_44ae00, 92, pool_a1a2d0)
RESERVE_GLOBAL(reserve_44cf70, append_44a150, 28, pool_a17ea0)
RESERVE_GLOBAL(reserve_44cfc0, append_44a1c0, 24, pool_a17eb4)
RESERVE_GLOBAL(reserve_44d010, append_44a230, 24, pool_a17ec8)
RESERVE_GLOBAL(reserve_44d060, append_44a2a0, 28, pool_a17edc)
RESERVE_GLOBAL(reserve_44d0b0, append_44a310, 8, pool_a17ef0)
RESERVE_GLOBAL(reserve_44d150, append_44a620, 16, pool_a1a500)
RESERVE_MEMBER(reserve_44d1a0, append_44bbb0, 16)
RESERVE_MEMBER(reserve_44d1f0, append_44bc20, 16)
RESERVE_GLOBAL(reserve_44d240, append_44bbb0, 16, pool_a17e00)
RESERVE_GLOBAL(reserve_44d290, append_44bc20, 16, pool_a17c74)
