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

extern const void *const methods_84f96c[];
extern const void *const methods_84fa30[];
extern const void *const methods_84facc[];
extern const void *const methods_84fb2c[];
extern const void *const methods_84ffe4[];
extern const void *const methods_850144[];
extern const void *const methods_85043c[];
extern const void *const methods_850620[];
extern const void *const methods_8506c8[];
extern const void *const methods_850740[];
extern void __fastcall parent_589070(struct Object *);
extern void __fastcall parent_58f020(struct Object *);

#define RELEASE_REFERENCE(Entry, Offset, DerivedMethods, CleanupParent, Category) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        static const union StackWord deleting = {1}; \
        struct Reference **slot = (struct Reference **)((unsigned char *)self + Offset); \
        struct Reference *item; \
        item = *slot; \
        self->methods = DerivedMethods; \
        if (item) { \
            --item->references; \
            if (!*(volatile unsigned short *)&item->references) item->methods->destroy(item, deleting.stack); \
            *slot = 0; \
        } \
        CleanupParent(self); \
        if (flags.value & 1) \
            manager->methods->release(manager, manager->methods, self, self->allocation_size, Category); \
        return self; \
    }

RELEASE_REFERENCE(release_5851f0, 228, methods_84f96c, parent_589070, 5)
RELEASE_REFERENCE(release_585830, 228, methods_84fa30, parent_589070, 5)
RELEASE_REFERENCE(release_585e40, 228, methods_84facc, parent_589070, 5)
RELEASE_REFERENCE(release_5863a0, 228, methods_84fb2c, parent_589070, 5)
RELEASE_REFERENCE(release_5899f0, 244, methods_84ffe4, parent_58f020, 5)
RELEASE_REFERENCE(release_58a130, 244, methods_850144, parent_58f020, 5)
RELEASE_REFERENCE(release_58c1a0, 244, methods_85043c, parent_58f020, 5)
RELEASE_REFERENCE(release_58cdd0, 244, methods_850620, parent_58f020, 5)
RELEASE_REFERENCE(release_58d9e0, 244, methods_8506c8, parent_58f020, 5)
RELEASE_REFERENCE(release_58e110, 244, methods_850740, parent_58f020, 5)
