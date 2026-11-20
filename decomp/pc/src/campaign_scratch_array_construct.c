struct Array { void *data; int count; unsigned capacity; void *original; int original_count; };
struct Manager;
struct Methods {
    void *reserved[9];
    void *(__fastcall *allocate)(struct Manager *, const struct Methods *, int);
};
struct Manager { const struct Methods *methods; int reserved; unsigned char *cursor; int remaining; };
struct Count { int value; };
extern struct Manager *manager;

#define CONSTRUCT_SCRATCH_ARRAY(Entry, ElementBytes) \
    struct Array *__fastcall Entry(struct Array *array, struct Count count) \
    { \
        int bytes; \
        void *data; \
        struct Manager *heap; \
        array->data = 0; \
        array->count = 0; \
        array->capacity = 0x80000000u; \
        heap = manager; \
        bytes = (int)((count.value * ElementBytes + 15u) & ~15u); \
        if (bytes > heap->remaining) { \
            data = heap->methods->allocate(heap, heap->methods, bytes); \
        } else { \
            unsigned char *previous = heap->cursor; \
            heap->cursor = previous + bytes; \
            heap->remaining -= bytes; \
            data = previous; \
        } \
        array->data = data; \
        array->capacity = count.value | 0x80000000u; \
        array->original_count = count.value; \
        array->original = data; \
        return array; \
    }

CONSTRUCT_SCRATCH_ARRAY(construct_513170, 160u)
CONSTRUCT_SCRATCH_ARRAY(construct_5145c0, 48u)
CONSTRUCT_SCRATCH_ARRAY(construct_515fe0, 256u)
CONSTRUCT_SCRATCH_ARRAY(construct_535bc0, 12u)
CONSTRUCT_SCRATCH_ARRAY(construct_554fe0, 8u)
CONSTRUCT_SCRATCH_ARRAY(construct_5550c0, 4u)
CONSTRUCT_SCRATCH_ARRAY(construct_555390, 8u)
CONSTRUCT_SCRATCH_ARRAY(construct_555470, 4u)
CONSTRUCT_SCRATCH_ARRAY(construct_555580, 16u)
CONSTRUCT_SCRATCH_ARRAY(construct_55a820, 4u)
CONSTRUCT_SCRATCH_ARRAY(construct_565170, 48u)
CONSTRUCT_SCRATCH_ARRAY(construct_591030, 16u)
CONSTRUCT_SCRATCH_ARRAY(construct_595ba0, 2u)
CONSTRUCT_SCRATCH_ARRAY(construct_6f8430, 4u)
CONSTRUCT_SCRATCH_ARRAY(construct_6f8510, 4u)
CONSTRUCT_SCRATCH_ARRAY(construct_6f85f0, 4u)
CONSTRUCT_SCRATCH_ARRAY(construct_6f86d0, 16u)
CONSTRUCT_SCRATCH_ARRAY(construct_6f87b0, 4u)
CONSTRUCT_SCRATCH_ARRAY(construct_6f8890, 1u)
CONSTRUCT_SCRATCH_ARRAY(construct_792160, 1u)
CONSTRUCT_SCRATCH_ARRAY(construct_5551a0, 32u)
CONSTRUCT_SCRATCH_ARRAY(construct_555280, 4u)
CONSTRUCT_SCRATCH_ARRAY(construct_6f8960, 4u)
