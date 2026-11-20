typedef unsigned short WChar;
struct Class;
struct Methods {
    void *reserved_00[24];
    void (__fastcall *bind)(struct Class *);
    void *reserved_64[8];
    const WChar *(__fastcall *cpp_name)(struct Class *);
};
struct Class {
    const struct Methods *methods;
    unsigned char reserved_04[0x18];
    unsigned flags;
    unsigned char reserved_20[8];
    struct Class *super;
    unsigned char reserved_2c[0xd8];
    void *binding;
};
extern const WChar binding_format[];
extern int __cdecl format_name(WChar *, const WChar *, ...);

void __fastcall bind_class(struct Class *self)
{
    WChar symbol[256];
    if (!self->binding) {
        if (self->flags & 0x04000000)
            format_name(symbol, binding_format, self->methods->cpp_name(self));
        if (!self->binding && self->super) {
            self->super->methods->bind(self->super);
            self->binding = self->super->binding;
        }
    }
}
