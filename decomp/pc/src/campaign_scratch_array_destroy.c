struct Array { void *data; int count; unsigned capacity; void *original; int original_count; };
struct Manager;
struct Methods {
    void *reserved[5];
    void (__fastcall *release)(struct Manager *, const struct Methods *, void *, unsigned, int);
    void *more[4];
    void (__fastcall *release_stack)(struct Manager *, void *, void *, unsigned);
};
struct Manager { const struct Methods *methods; int reserved; unsigned char *cursor; int remaining; int unused; void *marker; };
extern struct Manager *manager;

#define DESTROY_SCRATCH_ARRAY(Entry, ElementBytes, Category) \
    void __fastcall Entry(struct Array *array) \
    { \
        unsigned bytes = (array->original_count * ElementBytes + 15u) & ~15u; \
        void *original = array->original; \
        struct Manager *heap = manager; \
        if (bytes && (original == heap->marker || !heap->marker)) { \
            heap->methods->release_stack(heap, original, original, bytes); \
        } else { \
            heap->cursor -= bytes; \
            heap->remaining += bytes; \
        } \
        if (!(array->capacity & 0x80000000u)) { \
            heap = manager; \
            heap->methods->release(heap, heap->methods, array->data, (array->capacity & 0x7fffffffu) * ElementBytes, Category); \
        } \
    }

DESTROY_SCRATCH_ARRAY(destroy_5131e0, 160u, 18)
DESTROY_SCRATCH_ARRAY(destroy_514630, 48u, 18)
DESTROY_SCRATCH_ARRAY(destroy_516050, 256u, 18)
DESTROY_SCRATCH_ARRAY(destroy_535c30, 12u, 18)
DESTROY_SCRATCH_ARRAY(destroy_555050, 8u, 18)
DESTROY_SCRATCH_ARRAY(destroy_555130, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_555210, 32u, 18)
DESTROY_SCRATCH_ARRAY(destroy_5552f0, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_555400, 8u, 18)
DESTROY_SCRATCH_ARRAY(destroy_5554e0, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_5555f0, 16u, 18)
DESTROY_SCRATCH_ARRAY(destroy_55a890, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_5651e0, 48u, 18)
DESTROY_SCRATCH_ARRAY(destroy_5910a0, 16u, 18)
DESTROY_SCRATCH_ARRAY(destroy_595c10, 2u, 18)
DESTROY_SCRATCH_ARRAY(destroy_6f84a0, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_6f8580, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_6f8660, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_6f8740, 16u, 18)
DESTROY_SCRATCH_ARRAY(destroy_6f8820, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_6f8900, 1u, 18)
DESTROY_SCRATCH_ARRAY(destroy_6f89d0, 4u, 18)
DESTROY_SCRATCH_ARRAY(destroy_7921d0, 1u, 18)
