struct Shared;
struct Methods { void (__fastcall *destroy)(struct Shared *, unsigned, unsigned); };
struct Shared { const struct Methods *methods; unsigned references; };
struct Holder { struct Shared *shared; };
extern void __cdecl release_memory(struct Holder *);
struct Holder *__fastcall delete_holder(struct Holder *self, unsigned unused, unsigned flags)
{
    struct Shared *shared = self->shared;
    if (shared) {
        if (shared->references <= 1)
            shared->methods->destroy(shared, unused, 1);
        else
            --shared->references;
    }
    if (flags & 1) release_memory(self);
    return self;
}
