struct Object { const void *methods; unsigned short size; };
struct Array { void *storage; int count; unsigned capacity_flags; };
struct Flags { unsigned value; };
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
extern const void *const methods_84e538[];

#define RELEASE_BASE(Entry, ArrayOffset, ElementBytes, ArrayCategory, ObjectCategory, BaseMethods) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        struct Array *array = (struct Array *)((unsigned char *)self + ArrayOffset); \
        unsigned capacity; \
        capacity = array->capacity_flags; \
        if (!(capacity & 0x80000000u)) { \
            unsigned bytes = (capacity & 0x7fffffffu) * ElementBytes; \
            manager->methods->release(manager, manager->methods, array->storage, bytes, ArrayCategory); \
        } \
        self->methods = BaseMethods; \
        if (flags.value & 1) \
            manager->methods->release(manager, manager->methods, self, self->size, ObjectCategory); \
        return self; \
    }

#define RELEASE_DERIVED(Entry, ArrayOffset, ElementBytes, ArrayCategory, ObjectCategory, BaseMethods, DerivedMethods) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        struct Array *array = (struct Array *)((unsigned char *)self + ArrayOffset); \
        unsigned capacity; \
        self->methods = DerivedMethods; \
        capacity = array->capacity_flags; \
        if (!(capacity & 0x80000000u)) { \
            unsigned bytes = (capacity & 0x7fffffffu) * ElementBytes; \
            manager->methods->release(manager, manager->methods, array->storage, bytes, ArrayCategory); \
        } \
        self->methods = BaseMethods; \
        if (flags.value & 1) \
            manager->methods->release(manager, manager->methods, self, self->size, ObjectCategory); \
        return self; \
    }

RELEASE_DERIVED(release_5130b0, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_DERIVED(release_5133b0, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_DERIVED(release_5135f0, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_DERIVED(release_514800, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_DERIVED(release_514a40, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_DERIVED(release_516220, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_DERIVED(release_5164a0, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_BASE(release_518520, 12, 4, 18, 26, methods_7c1d60)
RELEASE_BASE(release_518570, 12, 4, 18, 26, methods_7c1d60)
RELEASE_DERIVED(release_531120, 12, 2, 18, 26, methods_7c1d60, methods_84b024)
RELEASE_DERIVED(release_531510, 12, 2, 18, 26, methods_7c1d60, methods_84b024)
RELEASE_BASE(release_532100, 12, 4, 18, 26, methods_7c1d60)
RELEASE_BASE(release_532190, 12, 4, 18, 26, methods_7c1d60)
RELEASE_BASE(release_5343a0, 12, 8, 18, 26, methods_7c1d60)
RELEASE_BASE(release_534430, 12, 8, 18, 26, methods_7c1d60)
RELEASE_DERIVED(release_534b20, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_DERIVED(release_534c90, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_DERIVED(release_535eb0, 12, 12, 18, 26, methods_7c1d60, methods_84aa90)
RELEASE_BASE(release_539ea0, 12, 8, 18, 26, methods_7c1d60)
RELEASE_BASE(release_539f30, 12, 8, 18, 26, methods_7c1d60)
RELEASE_DERIVED(release_578430, 8, 4, 18, 12, methods_7c1d60, methods_84e538)
