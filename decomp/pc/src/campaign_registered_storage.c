struct Object { const void *methods; unsigned short size_flags; };
struct Manager;
struct Methods { void *reserved[5]; void (__fastcall *release)(struct Manager *, unsigned, struct Object *, unsigned, unsigned); };
struct Manager { const struct Methods *methods; };
extern struct Manager *manager;
extern const void *const derived_methods[], *const base_methods[];
extern void __fastcall set_registration(struct Object *, unsigned, unsigned);

static __forceinline struct Object *destroy_registered(struct Object *self, unsigned unused, unsigned flags, unsigned category)
{
    self->methods = derived_methods;
    if (self->size_flags & 0x8000u) {
        self->size_flags &= 0x7fffu;
        set_registration(self, unused, 0);
    }
    self->methods = base_methods;
    if (flags & 1) {
        unsigned bytes = self->size_flags;
        manager->methods->release(manager, bytes, self, bytes, category);
    }
    return self;
}

struct Object *__fastcall release_category_16(struct Object *self, unsigned unused_edx, unsigned flags)
{
    return destroy_registered(self, unused_edx, flags, 16);
}

struct Object *__fastcall release_category_25(struct Object *self, unsigned unused_edx, unsigned flags)
{
    return destroy_registered(self, unused_edx, flags, 25);
}

struct Object *__fastcall release_category_34(struct Object *self, unsigned unused_edx, unsigned flags)
{
    return destroy_registered(self, unused_edx, flags, 34);
}

struct Object *__fastcall release_category_38(struct Object *self, unsigned unused_edx, unsigned flags)
{
    return destroy_registered(self, unused_edx, flags, 38);
}

struct Object *__fastcall release_category_39(struct Object *self, unsigned unused_edx, unsigned flags)
{
    return destroy_registered(self, unused_edx, flags, 39);
}
