struct Block { void *base; unsigned char *cursor; unsigned used, capacity; };
struct Pool { unsigned reserved; struct Block *first, *end, *limit; unsigned char active; };

extern void *__cdecl allocate_block(unsigned bytes, unsigned mode);
extern struct Pool pool_a17b08;
extern struct Pool pool_a17b44;
extern struct Pool pool_a17b80;
extern struct Pool pool_a17bbc;
extern struct Pool pool_a17c24;
extern struct Pool pool_a17cf0;
extern struct Pool pool_a17fe4;
extern struct Pool pool_a18020;
extern struct Pool pool_a1a0ec;
extern struct Pool pool_a1a128;
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

#define TAKE_ENTRY(Entry, Pool, ElementBytes) \
    void *__cdecl Entry(unsigned bytes) \
    { \
        if (Pool.active) { \
            struct Block *last = Pool.end - 1; \
            unsigned char *result; \
            ++last->used; \
            result = last->cursor; \
            last->cursor = result + ElementBytes; \
            return result; \
        } \
        return allocate_block(bytes, 0); \
    }

TAKE_ENTRY(take_4271d0, pool_a17b44, 36)
TAKE_ENTRY(take_427290, pool_a18020, 80)
TAKE_ENTRY(take_4272d0, pool_a1a128, 52)
TAKE_ENTRY(take_427310, pool_a1a258, 88)
TAKE_ENTRY(take_427350, pool_a1a21c, 92)
TAKE_ENTRY(take_4273d0, pool_a17bbc, 80)
TAKE_ENTRY(take_427410, pool_a1a348, 84)
TAKE_ENTRY(take_427450, pool_a1a384, 52)
TAKE_ENTRY(take_427490, pool_a1a294, 92)
TAKE_ENTRY(take_4274d0, pool_a1a1e0, 40)
TAKE_ENTRY(take_427510, pool_a1a0ec, 204)
TAKE_ENTRY(take_427550, pool_a1a1a4, 16)
TAKE_ENTRY(take_427590, pool_a1a3c0, 28)
TAKE_ENTRY(take_4275d0, pool_a17c24, 208)
TAKE_ENTRY(take_427610, pool_a17cf0, 36)
TAKE_ENTRY(take_427650, pool_a17b08, 192)
TAKE_ENTRY(take_427690, pool_a17b80, 84)
TAKE_ENTRY(take_4276d0, pool_a1a3fc, 84)
TAKE_ENTRY(take_427710, pool_a1a2d0, 92)
TAKE_ENTRY(take_427750, pool_a17fe4, 28)
