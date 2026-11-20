struct Object { const void *methods; unsigned short allocation_size; };
struct Flags { unsigned value; };
struct Manager;
struct ManagerMethods {
    void *reserved[5];
    /* The x86 call loads the size into EDX and passes object, size and category on the stack. */
    void (__fastcall *release)(struct Manager *, unsigned edx_size, struct Object *, unsigned bytes, unsigned category);
};
struct Manager { const struct ManagerMethods *methods; };
extern struct Manager *manager;

extern const void *const methods_7c1d60[];
extern const void *const methods_84f32c[];
extern const void *const methods_84f7f8[];
extern const void *const methods_84fea0[];
extern const void *const methods_84ff38[];
extern const void *const methods_850838[];
extern const void *const methods_850938[];
extern const void *const methods_851310[];
extern void __fastcall cleanup_08a4d0(struct Object *);
extern void __fastcall cleanup_5809e0(struct Object *);
extern void __fastcall cleanup_584ad0(struct Object *);
extern void __fastcall cleanup_58f120(struct Object *);

#define RELEASE_MEMBER(Entry, BaseMethods, MemberMethods, Offset, Category) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        struct Object *member = self ? (struct Object *)((unsigned char *)self + Offset) : 0; \
        member->methods = MemberMethods; \
        self->methods = BaseMethods; \
        if (flags.value & 1) { \
            unsigned bytes = self->allocation_size; \
            manager->methods->release(manager, bytes, self, bytes, Category); \
        } \
        return self; \
    }

#define RELEASE_CLEANED(Entry, BaseMethods, Cleanup, Category) \
    struct Object *__fastcall Entry(struct Object *self, struct Flags flags) \
    { \
        self->methods = BaseMethods; \
        Cleanup(self); \
        if (flags.value & 1) { \
            unsigned bytes = self->allocation_size; \
            manager->methods->release(manager, bytes, self, bytes, Category); \
        } \
        return self; \
    }
RELEASE_MEMBER(release_57f290, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_5802b0, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_CLEANED(release_5810c0, methods_84f32c, cleanup_584ad0, 5)
RELEASE_MEMBER(release_5834f0, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_583560, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_5835d0, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_583640, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_5836b0, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_583720, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_CLEANED(release_584950, methods_84f7f8, cleanup_5809e0, 5)
RELEASE_CLEANED(release_588350, methods_84fea0, cleanup_5809e0, 5)
RELEASE_MEMBER(release_588900, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_588970, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_5889e0, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_588a50, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_588ac0, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_588b30, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_588ba0, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_CLEANED(release_589080, methods_84ff38, cleanup_5809e0, 5)
RELEASE_MEMBER(release_5894a0, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_589510, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_MEMBER(release_589640, methods_7c1d60, methods_7c1d60, 8, 5)
RELEASE_CLEANED(release_58eba0, methods_850838, cleanup_58f120, 5)
RELEASE_CLEANED(release_58eeb0, methods_850938, cleanup_58f120, 5)
RELEASE_CLEANED(release_59a350, methods_851310, cleanup_08a4d0, 10)
