/* Tree head and node links use the same sentinel as leaf pointers. */
#define TREE_LAYOUT(N, Payload) \
    struct N { struct N *left, *parent, *right; unsigned char value[Payload]; unsigned char color, sentinel; }; \
    struct Tree_##N { void *allocator; struct N *head; }; \
    struct Ref_##N { struct N *node; }

#define ROTATE(Entry, N, Near, Far) \
    void __fastcall Entry(struct Tree_##N *tree, struct Ref_##N root) \
    { \
        struct N *node = root.node; \
        struct N *pivot = node->Near; \
        node->Near = pivot->Far; \
        if (!pivot->Far->sentinel) pivot->Far->parent = node; \
        pivot->parent = node->parent; \
        if (node == tree->head->parent) tree->head->parent = pivot; \
        else if (node == node->parent->Far) node->parent->Far = pivot; \
        else node->parent->Near = pivot; \
        pivot->Far = node; \
        node->parent = pivot; \
    }
