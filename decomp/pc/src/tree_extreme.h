/* Tree head and node links use the same sentinel as leaf pointers. */
#define TREE_LAYOUT(N, Payload) \
    struct N { struct N *left, *parent, *right; unsigned char value[Payload]; unsigned char color, sentinel; }; \
    struct Tree_##N { void *allocator; struct N *head; }; \
    struct Ref_##N { struct N *node; }

#define EXTREME(Entry, N, Child) \
    struct N *__cdecl Entry(struct N *node) \
    { \
        while (!node->Child->sentinel) node = node->Child; \
        return node; \
    }
