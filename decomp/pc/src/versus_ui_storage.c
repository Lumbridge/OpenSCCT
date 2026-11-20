struct Owner {
    const void *methods;
    unsigned prefix[280];
    unsigned mode, first, second;
    unsigned char count;
    void **items;
};
extern const void *owner_methods[];
extern void __fastcall initialize_base(struct Owner *);
extern void __cdecl release_memory(void *);

struct Owner *__fastcall initialize_owner(struct Owner *owner)
{
    initialize_base(owner);
    owner->methods = owner_methods;
    owner->mode = 1;
    owner->first = 0;
    owner->second = 0;
    owner->count = 0;
    owner->items = 0;
    return owner;
}

void __fastcall release_items(struct Owner *owner)
{
    if (owner->items) {
        int i;
        for (i = 0; i < owner->count; ++i)
            release_memory(owner->items[i]);
        release_memory(owner->items);
        owner->items = 0;
    }
}

struct Service;
extern struct Service *ui_service;
extern void __fastcall destroy_service(struct Service *);

void __cdecl release_service(void)
{
    struct Service *service = ui_service;
    if (ui_service) {
        destroy_service(ui_service);
        release_memory(service);
    }
    ui_service = 0;
}
