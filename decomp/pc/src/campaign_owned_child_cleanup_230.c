struct Child;
struct Owner { const void *methods; unsigned reserved[139]; struct Child *child; }; /* child at 0x230 */
struct Flags { unsigned value; };
extern const void *const owner_methods[];
extern void __fastcall destroy_child(struct Child *);
extern void __fastcall destroy_base(struct Owner *);
extern void __cdecl release(void *);
struct Owner *__fastcall destroy_owner(struct Owner *self, struct Flags flags)
{
    struct Child *child;
    self->methods = owner_methods;
    child = self->child;
    if (child) {
        destroy_child(child);
        release(child);
    }
    destroy_base(self);
    if (flags.value & 1) release(self);
    return self;
}
