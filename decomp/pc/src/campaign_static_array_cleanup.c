struct Array { void *data; unsigned count, capacity; };
struct Allocator;
struct Pointer { void *value; };
struct Count { unsigned value; };
struct Methods {
    void *reserved[2];
    void (__fastcall *release)(struct Allocator *, struct Pointer);
};
struct Allocator { const struct Methods *methods; };
extern struct Array storage;
extern struct Allocator *engine_allocator;
extern void __fastcall remove_range(struct Array *, float first_bits, struct Count count, float width_bits);

static __forceinline void destroy_storage(float element_size_bits)
{
    struct Count count;
    count.value = storage.count;
    remove_range(&storage, 0.0f, count, element_size_bits);
    if (storage.data) {
        struct Pointer buffer = {storage.data};
        engine_allocator->methods->release(engine_allocator, buffer);
    }
    storage.data = 0;
    storage.capacity = 0;
    storage.count = 0;
}

/* A fastcall float argument occupies the same four-byte stack slot as
   the original integer element size. Each subnormal encodes that size. */
#define CLEAR_ARRAY(Width) \
    void __cdecl clear_##Width(void) \
    { \
        destroy_storage(Width * 1.40129846e-45f); \
    }

CLEAR_ARRAY(1)
CLEAR_ARRAY(4)
CLEAR_ARRAY(12)
CLEAR_ARRAY(16)
CLEAR_ARRAY(40)
CLEAR_ARRAY(48)
CLEAR_ARRAY(64)
