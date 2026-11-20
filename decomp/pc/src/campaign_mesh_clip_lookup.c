struct Name { int value; };
struct Clip { struct Name name; unsigned char reserved[0x44]; };
struct Bank {
    unsigned char reserved[0x44];
    struct Clip *clips;
    int count;
};
struct BankValue { struct Bank *ptr; };
struct Resource { unsigned char reserved[0x210]; struct Bank *default_bank; };
struct Mesh;
struct Methods {
    void *reserved[34];
    struct Resource *(__fastcall *resource)(struct Mesh *);
};
struct Mesh {
    const struct Methods *methods;
    unsigned char reserved[0x74];
    struct Bank **banks;
    int count;
};
extern int __fastcall register_bank(struct Mesh *, struct BankValue);

struct Clip *__fastcall find_clip(struct Mesh *mesh, struct Name name)
{
    int i, j;
    if (!mesh->count && mesh->methods->resource(mesh)->default_bank) {
        struct BankValue bank;
        bank.ptr = mesh->methods->resource(mesh)->default_bank;
        register_bank(mesh, bank);
    }
    for (i = 0; i < mesh->count; ++i) {
        struct Bank *bank = mesh->banks[i];
        if (bank) {
            for (j = 0; j < bank->count; ++j) {
                if (name.value == bank->clips[j].name.value)
                    return &mesh->banks[i]->clips[j];
            }
        }
    }
    return 0;
}
