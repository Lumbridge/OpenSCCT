struct Array { void *data; int size; unsigned capacity; };
struct Manager;
struct Methods {
    void *reserved[5];
    void (__fastcall *release)(struct Manager *, void *, void *, unsigned, unsigned);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;

#define RELEASE_ARRAY_STORAGE(Entry, ElementBytes, Category) \
    void __fastcall Entry(struct Array *array) \
    { \
        unsigned capacity = array->capacity; \
        if (!(capacity & 0x80000000u)) { \
            struct Manager *heap; \
            void *data; \
            const struct Methods *methods; \
            heap = manager; \
            data = array->data; \
            methods = heap->methods; \
            methods->release(heap, data, data, (capacity & 0x7fffffffu) * ElementBytes, Category); \
        } \
    }

RELEASE_ARRAY_STORAGE(release_1072d0, 12u, 18)
RELEASE_ARRAY_STORAGE(release_107440, 12u, 18)
RELEASE_ARRAY_STORAGE(release_1d3150, 144u, 18)
RELEASE_ARRAY_STORAGE(release_1d3180, 144u, 18)
RELEASE_ARRAY_STORAGE(release_4fd4b0, 12u, 18)
RELEASE_ARRAY_STORAGE(release_4fd610, 12u, 18)
RELEASE_ARRAY_STORAGE(release_4fd770, 12u, 18)
RELEASE_ARRAY_STORAGE(release_50ad50, 48u, 18)
RELEASE_ARRAY_STORAGE(release_50b020, 48u, 18)
RELEASE_ARRAY_STORAGE(release_50b570, 48u, 18)
RELEASE_ARRAY_STORAGE(release_512dd0, 12u, 18)
RELEASE_ARRAY_STORAGE(release_512e00, 160u, 18)
RELEASE_ARRAY_STORAGE(release_512f90, 12u, 18)
RELEASE_ARRAY_STORAGE(release_512fc0, 160u, 18)
RELEASE_ARRAY_STORAGE(release_5144c0, 48u, 18)
RELEASE_ARRAY_STORAGE(release_5144f0, 48u, 18)
RELEASE_ARRAY_STORAGE(release_54d650, 80u, 18)
RELEASE_ARRAY_STORAGE(release_54d820, 80u, 18)
RELEASE_ARRAY_STORAGE(release_54dfa0, 80u, 18)
RELEASE_ARRAY_STORAGE(release_553830, 12u, 18)
RELEASE_ARRAY_STORAGE(release_554830, 12u, 18)
RELEASE_ARRAY_STORAGE(release_564b60, 48u, 18)
RELEASE_ARRAY_STORAGE(release_564de0, 48u, 18)
RELEASE_ARRAY_STORAGE(release_578b30, 80u, 18)
RELEASE_ARRAY_STORAGE(release_578bc0, 80u, 18)
RELEASE_ARRAY_STORAGE(release_579b80, 12u, 18)
RELEASE_ARRAY_STORAGE(release_579c50, 12u, 18)
RELEASE_ARRAY_STORAGE(release_593d60, 20u, 18)
RELEASE_ARRAY_STORAGE(release_5947c0, 20u, 18)
RELEASE_ARRAY_STORAGE(release_595ab0, 20u, 18)
RELEASE_ARRAY_STORAGE(release_595b40, 20u, 18)
RELEASE_ARRAY_STORAGE(release_59b440, 48u, 18)
RELEASE_ARRAY_STORAGE(release_5a1570, 192u, 18)
RELEASE_ARRAY_STORAGE(release_5a15a0, 192u, 18)
RELEASE_ARRAY_STORAGE(release_5a15d0, 192u, 18)
RELEASE_ARRAY_STORAGE(release_6fed30, 48u, 18)
RELEASE_ARRAY_STORAGE(release_6ff1b0, 48u, 18)
