struct Object { const void *methods; };
struct Array { void *data; int size; unsigned capacity; };
struct Manager;
struct Methods {
    void *reserved[5];
    void (__fastcall *release)(struct Manager *, const struct Methods *, void *, unsigned, unsigned);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;

extern const void *const methods_7c1d60[];
extern const void *const methods_84aa90[];
extern const void *const methods_84b024[];
extern const void *const methods_84bc90[];
extern const void *const methods_84e538[];

#define CLEANUP_DERIVED(Entry, Offset, ElementBytes, Category, BaseMethods, DerivedMethods) \
    void __fastcall Entry(struct Object *self) \
    { \
        struct Array *array = (struct Array *)((unsigned char *)self + Offset); \
        unsigned capacity; \
        self->methods = DerivedMethods; \
        capacity = array->capacity; \
        if (!(capacity & 0x80000000u)) \
            manager->methods->release(manager, manager->methods, array->data, (capacity & 0x7fffffffu) * ElementBytes, Category); \
        self->methods = BaseMethods; \
    }

#define CLEANUP_BASE(Entry, Offset, ElementBytes, Category, BaseMethods) \
    void __fastcall Entry(struct Object *self) \
    { \
        struct Array *array = (struct Array *)((unsigned char *)self + Offset); \
        unsigned capacity; \
        capacity = array->capacity; \
        if (!(capacity & 0x80000000u)) \
            manager->methods->release(manager, manager->methods, array->data, (capacity & 0x7fffffffu) * ElementBytes, Category); \
        self->methods = BaseMethods; \
    }

CLEANUP_DERIVED(destroy_513070, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_DERIVED(destroy_513130, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_DERIVED(destroy_513250, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_DERIVED(destroy_514580, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_DERIVED(destroy_5146a0, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_DERIVED(destroy_515f10, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_DERIVED(destroy_5160c0, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_BASE(destroy_518380, 12, 4u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_5183f0, 12, 4u, 18, methods_7c1d60)
CLEANUP_DERIVED(destroy_5309a0, 12, 2u, 18, methods_7c1d60, methods_84b024)
CLEANUP_DERIVED(destroy_5313a0, 12, 2u, 18, methods_7c1d60, methods_84b024)
CLEANUP_BASE(destroy_531f60, 12, 4u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_531f90, 12, 4u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_534200, 12, 8u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_534230, 12, 8u, 18, methods_7c1d60)
CLEANUP_DERIVED(destroy_534970, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_DERIVED(destroy_5349b0, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_DERIVED(destroy_535e10, 12, 12u, 18, methods_7c1d60, methods_84aa90)
CLEANUP_BASE(destroy_539d10, 12, 8u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_539d40, 12, 8u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_555870, 8, 4u, 18, methods_84bc90)
CLEANUP_DERIVED(destroy_5783b0, 8, 4u, 18, methods_7c1d60, methods_84e538)
CLEANUP_BASE(destroy_57f3c0, 8, 4u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_583b40, 8, 4u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_589020, 8, 4u, 18, methods_7c1d60)
CLEANUP_BASE(destroy_5897f0, 8, 4u, 18, methods_7c1d60)
