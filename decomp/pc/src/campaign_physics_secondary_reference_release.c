struct Object { const void *methods; unsigned short allocation_size; };
struct Reference;
/* The virtual destructor receives one 32-bit deletion flag on the stack. */
union StackWord { unsigned value; float stack; };
struct RefMethods { void (__fastcall *destroy)(struct Reference *, float); };
struct Reference { const struct RefMethods *methods; unsigned short size; unsigned short references; };
struct Flags { unsigned value; };
struct Manager;
struct Methods {
    void *reserved[5];
    void (__fastcall *release)(struct Manager *, const struct Methods *, void *, unsigned, unsigned);
};
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;

extern const void *const methods_84caa8[];
extern const void *const methods_84cab8[];
extern const void *const methods_84cb20[];
extern const void *const methods_84cb30[];
extern const void *const methods_84d674[];
extern const void *const methods_84d684[];
extern void __fastcall parent_56d070(struct Object *);

#define RELEASE_REFERENCE(Entry, Offset, DerivedMethods, CleanupParent, Category, Secondary) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        static const union StackWord deleting = {1}; \
        struct Reference **slot = (struct Reference **)((unsigned char *)self + Offset); \
        struct Reference *item; \
        item = *slot; \
        self->methods = DerivedMethods; \
        *(const void **)((unsigned char *)self + 8) = Secondary; \
        if (!--item->references) \
            item->methods->destroy(item, deleting.stack); \
        CleanupParent(self); \
        if (flags.value & 1) \
            manager->methods->release(manager, manager->methods, self, self->allocation_size, Category); \
        return self; \
    }

RELEASE_REFERENCE(release_55b3f0, 32, methods_84cab8, parent_56d070, 39, methods_84caa8)
RELEASE_REFERENCE(release_55b730, 32, methods_84cb30, parent_56d070, 39, methods_84cb20)
RELEASE_REFERENCE(release_561140, 32, methods_84d684, parent_56d070, 39, methods_84d674)
