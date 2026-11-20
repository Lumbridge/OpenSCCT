struct Owner;
struct Entry;
struct Handler;
struct EntryArgument { struct Entry *entry; };
struct Word { unsigned value; };
typedef unsigned char (__fastcall *Method)(struct Handler *, struct EntryArgument, struct Word);
struct Handler { Method *methods; };
struct Entry { unsigned prefix[11]; struct Handler *handler; };
struct Owner { unsigned prefix[16]; struct Entry *entry; };
extern void __fastcall prepare_entries(struct Owner *);

#define DISPATCH(Slot) \
    unsigned char __fastcall dispatch_##Slot(struct Owner *owner, unsigned unused, struct Word argument) \
    { \
        if (owner->entry) { \
            struct EntryArgument selected; \
            struct Handler *handler; \
            prepare_entries(owner); \
            selected.entry = owner->entry; \
            handler = selected.entry->handler; \
            return handler->methods[Slot](handler, selected, argument); \
        } \
        return 0; \
    }

DISPATCH(8)
DISPATCH(9)
