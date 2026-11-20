struct Word { unsigned value; };
struct Source { const struct Word *word; };
struct Allocation { void *address; unsigned count; };
extern void __stdcall release_storage(void *address);

#define COPY_WORD(Entry) \
    struct Word *__fastcall Entry(struct Word *out, struct Source from) \
    { \
        *out = *from.word; \
        return out; \
    }

#define RELEASE_ALLOCATION(Entry) \
    void __fastcall Entry(void *pool, struct Allocation allocation) \
    { \
        release_storage(allocation.address); \
    }

COPY_WORD(copy_235f10)
RELEASE_ALLOCATION(release_2c0f80)
