struct Rectangle { short left, right, top, bottom; };
struct BoundsArgument { struct Rectangle *bounds; };
struct Handler;
typedef void (__fastcall *GetBounds)(struct Handler *, struct BoundsArgument);
struct Handler { GetBounds *methods; };
struct Entry {
    unsigned reserved;
    unsigned char enabled;
    unsigned char padding[39];
    struct Handler *handler;
};
struct Point { short x, y; };
struct Owner {
    unsigned prefix[10];
    struct Entry **first;
    struct Entry **last;
};
extern unsigned char __cdecl eligible(struct Entry *);

static __forceinline int entry_count(struct Owner *owner)
{
    if (!owner->first)
        return 0;
    return owner->last - owner->first;
}

static __forceinline int contains(const struct Rectangle *bounds, const struct Point *point)
{
    return point->x >= bounds->left && point->x <= bounds->right &&
           point->y >= bounds->top && point->y <= bounds->bottom;
}


struct Entry *__fastcall hit_test(struct Owner *owner, unsigned unused, const struct Point *point)
{
    int i;
    for (i = entry_count(owner) - 1; i >= 0; --i) {
        struct Entry *entry = owner->first[i];
        if (eligible(entry) && entry->enabled) {
            struct Rectangle bounds;
            struct BoundsArgument output;
            bounds.left = 0;
            bounds.right = 0;
            bounds.top = 0;
            bounds.bottom = 0;
            output.bounds = &bounds;
            entry->handler->methods[4](entry->handler, output);
            if (contains(&bounds, point))
                return entry;
        }
    }
    return 0;
}
