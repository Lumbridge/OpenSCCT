struct Registration {
    const unsigned short *name;
    void *handler;
};

extern struct Registration registrations[];
extern int __cdecl compare_names(const unsigned short *left, const unsigned short *right);

void **__cdecl find_handler(const unsigned short *name)
{
    int i;
    for (i = 0; registrations[i].name; ++i) {
        if (compare_names(name, registrations[i].name) == 0)
            return &registrations[i].handler;
    }
    return 0;
}
