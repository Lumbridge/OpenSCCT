struct Child;
struct Group { unsigned allocator; struct Child **first, **end, **limit; };
struct Groups { unsigned char prefix[16]; struct Group lists[]; };

unsigned __fastcall child_count(struct Groups *groups, unsigned unused, unsigned index)
{
    struct Group *group = &groups->lists[index];
    if (!group->first) return 0;
    return group->end - group->first;
}

struct Child *__fastcall child_at(struct Groups *groups, unsigned unused, unsigned child, unsigned group)
{
    return groups->lists[group].first[child];
}
