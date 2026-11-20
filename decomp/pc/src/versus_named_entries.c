__declspec(dllimport) int __cdecl _stricmp(const char *, const char *);
struct Name {
    unsigned allocator;
    union { char local[16]; char *heap; } buffer;
    unsigned length, capacity;
};
struct Entry { unsigned prefix[16]; struct Name name; };
struct Node { struct Node *next, *previous; struct Entry *entry; };
struct List { unsigned reserved; struct Node *sentinel; };

static __forceinline const char *text(const struct Name *name)
{
    if (name->capacity >= 16) return name->buffer.heap;
    return name->buffer.local;
}

static __forceinline struct Node *begin(struct List *list)
{
    if (!list->sentinel) return 0;
    return list->sentinel->next;
}

struct Entry *__cdecl find_named_entry(struct List *list, const char *name)
{
    struct Node *node;
    for (node = begin(list); node != list->sentinel; node = node->next) {
        if (_stricmp(text(&node->entry->name), name) == 0)
            return node->entry;
    }
    return 0;
}
