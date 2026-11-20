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

extern const void *const methods_84ce64[];
extern const void *const methods_84ce74[];
extern const void *const methods_84f3a8[];
extern const void *const methods_850980[];
extern const void *const methods_85099c[];
extern void __fastcall parent_56d070(struct Object *);
extern void __fastcall parent_5809e0(struct Object *);

#define RELEASE_REFERENCE(Entry, Offset, DerivedMethods, CleanupParent, Category, ClearSlot, Secondary) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        static const union StackWord deleting = {1}; \
        struct Reference **slot = (struct Reference **)((unsigned char *)self + Offset); \
        struct Reference *item; \
        item = *slot; \
        self->methods = DerivedMethods; \
        if (Secondary) *(const void **)((unsigned char *)self + 8) = Secondary; \
        if (item) { \
            --item->references; \
            if (!*(volatile unsigned short *)&item->references) item->methods->destroy(item, deleting.stack); \
        } \
        if (ClearSlot) *slot = 0; \
        CleanupParent(self); \
        if (flags.value & 1) \
            manager->methods->release(manager, manager->methods, self, self->allocation_size, Category); \
        return self; \
    }

RELEASE_REFERENCE(release_55c760, 232, methods_84ce74, parent_56d070, 39, 0, methods_84ce64)
RELEASE_REFERENCE(release_582cd0, 304, methods_84f3a8, parent_5809e0, 5, 0, 0)
RELEASE_REFERENCE(release_58f0c0, 232, methods_850980, parent_5809e0, 5, 1, 0)
RELEASE_REFERENCE(release_58f250, 312, methods_85099c, parent_5809e0, 5, 0, 0)
