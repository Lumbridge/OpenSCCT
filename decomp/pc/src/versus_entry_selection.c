struct Entry { unsigned char reserved[0xd4]; unsigned char flags; };
struct Owner {
    unsigned prefix[10];
    struct Entry **first;
    struct Entry **last;
    unsigned reserved[4];
    struct Entry *selected;
};
extern unsigned char __cdecl eligible(struct Entry *);

static __forceinline int entry_count(struct Owner *owner)
{
    if (!owner->first)
        return 0;
    return owner->last - owner->first;
}

void __fastcall select_eligible(struct Owner *owner)
{
    int i;
    for (i = 0; i < entry_count(owner); ++i) {
        struct Entry *entry = ((volatile struct Owner *)owner)->first[i];
        if (eligible(entry) && (entry->flags & 8)) {
            owner->selected = entry;
            break;
        }
    }
}
